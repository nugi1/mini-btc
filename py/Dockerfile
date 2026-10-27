# The demo driver: the project's own wallet and miner code, run in a loop.
FROM python:3.13-slim
RUN pip install --no-cache-dir "coincurve>=21,<22"
WORKDIR /py
COPY btc_rpc btc_rpc
COPY wallet wallet
COPY miner miner
COPY demo demo
ENV PYTHONUNBUFFERED=1
CMD ["python", "-m", "demo"]
