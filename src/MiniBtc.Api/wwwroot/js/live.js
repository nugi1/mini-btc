// Live chain events over SignalR (/hubs/chain). Views subscribe with on(); the connection reconnects
// on its own and reports its state, which the top bar shows.

const listeners = new Map();
/** The last 50 events since the page loaded, newest first, so the activity feed survives navigation. */
export const history = [];
const stateListeners = new Set();
const EVENTS = ["BlockConnected", "BlockDisconnected", "TxAccepted", "TxRejected", "TxRemoved"];
let state = "connecting";

export function on(event, handler) {
  if (!listeners.has(event)) listeners.set(event, new Set());
  listeners.get(event).add(handler);
  return () => listeners.get(event).delete(handler);
}

export function onState(handler) {
  stateListeners.add(handler);
  handler(state);
  return () => stateListeners.delete(handler);
}

function setState(next) {
  state = next;
  stateListeners.forEach((handler) => handler(state));
}

function emit(event, payload) {
  if (EVENTS.includes(event)) {
    history.unshift({ event, payload, at: new Date() });
    history.length = Math.min(history.length, 50);
  }
  (listeners.get(event) ?? []).forEach((handler) => {
    try { handler(payload); } catch (error) { console.error(`live ${event} handler failed`, error); }
  });
  (listeners.get("*") ?? []).forEach((handler) => handler(event, payload));
}

export async function start() {
  if (!window.signalR) { setState("unavailable"); return; }
  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chain")
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  EVENTS.forEach((event) => connection.on(event, (payload) => emit(event, payload)));
  connection.onreconnecting(() => setState("reconnecting"));
  connection.onreconnected(() => { setState("live"); emit("Reconnected", {}); });
  connection.onclose(() => setState("offline"));

  for (let attempt = 0; ; attempt++) {
    try {
      await connection.start();
      setState("live");
      return;
    } catch {
      setState("offline");
      await new Promise((resolve) => setTimeout(resolve, Math.min(30000, 2000 * 2 ** attempt)));
    }
  }
}
