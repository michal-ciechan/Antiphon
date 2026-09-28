FROM debian:trixie-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium poppler-utils fonts-noto-color-emoji \
    && rm -rf /var/lib/apt/lists/*
