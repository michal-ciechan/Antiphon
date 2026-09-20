# CARD-0490 native PC assets

Source-free guest disk, QEMU bundle, SDK 10.0.204, net9 runtime and offline NuGet inputs are pinned in `assets.lock.json`. FAT is transport only; product tests build on guest ext4.

Ordinary Code uses `scripts/test-card0490-native.ps1 -Ordinary -Phase baseline`. Sourced Mutation requires BindingFile and never downgrades to ordinary.

V-7 isolated live turn: copy `card0490-live.example.json` or let `scripts/verify-phone-home-grok.ps1` allocate ports into `.antiphon/card0490-live.json`. The script copies `auth.json` from primary `GROK_HOME` (`GROK_HOME` env or `%USERPROFILE%\.grok`) into a throwaway directory and mounts that copy read-only at `/state/grok`. Do not bind-mount the primary store. `-Placement server2` sets `phoneHomeServerOrigin` to `http://<desktop-tailscale-ipv4>:<port>` instead of `host.docker.internal`. Do not put tokens in the JSON file.
