// All blocks, newest first, paged by height.
import { api } from "../api.js";
import { bytes, dateTime, html, number, timeAgo } from "../format.js";
import * as live from "../live.js";
import { amount, card, errorState, hashLink, loading, pageHead, table } from "../ui.js";

const PAGE = 25;

export async function mount(root) {
  let blocks = [];
  root.innerHTML = html`${pageHead({ title: "Blocks", sub: "The node's active chain, highest first. A reorg removes blocks from this list." })}
    <div id="list">${loading(8)}</div>`.html;

  const render = (hasMore) => {
    root.querySelector("#list").innerHTML = card({
      title: `${number(blocks[0]?.height ?? 0)} blocks above genesis`,
      body: table([
        { label: "Height", render: (b) => html`<a href="#/block/${b.hash}"><strong>${number(b.height)}</strong></a>` },
        { label: "Hash", render: (b) => hashLink("block", b.hash) },
        { label: "Time", render: (b) => html`<span title="${dateTime(b.time)}">${timeAgo(b.time)}</span>` },
        { label: "Transactions", num: true, render: (b) => number(b.transactionCount) },
        { label: "Size", num: true, render: (b) => bytes(b.size) },
        { label: "Fees", num: true, render: (b) => amount(b.fees, { trim: true }) },
      ], blocks, { empty: "No blocks yet." }),
      foot: hasMore ? html`<span>Showing ${number(blocks.length)}</span><button class="btn" id="more" type="button">Load older blocks</button>`
                    : html`<span>Showing all ${number(blocks.length)}</span><span></span>`,
    }).html;
    root.querySelector("#more")?.addEventListener("click", loadMore);
  };

  async function loadMore() {
    const before = blocks.length ? blocks[blocks.length - 1].height : undefined;
    const page = await api.blocks(PAGE, before);
    blocks = blocks.concat(page);
    render(page.length === PAGE && page[page.length - 1].height > 0);
  }

  async function reload() {
    try {
      const page = await api.blocks(Math.max(PAGE, blocks.length));
      blocks = page;
      render(page.length >= PAGE && page[page.length - 1].height > 0);
    } catch (error) {
      root.querySelector("#list").innerHTML = errorState("Blocks unavailable", error.message).html;
    }
  }

  await reload();
  const offs = [live.on("BlockConnected", reload), live.on("BlockDisconnected", reload)];
  return () => offs.forEach((off) => off());
}
