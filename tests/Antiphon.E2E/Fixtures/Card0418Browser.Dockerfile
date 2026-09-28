FROM debian:trixie-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium poppler-utils \
    && rm -rf /var/lib/apt/lists/*
