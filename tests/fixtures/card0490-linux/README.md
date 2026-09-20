# CARD-0490 native PC assets

Source-free guest disk, QEMU bundle, SDK 10.0.204, net9 runtime and offline NuGet inputs are pinned in `assets.lock.json`. FAT is transport only; product tests build on guest ext4.

Ordinary Code uses `scripts/test-card0490-native.ps1 -Ordinary -Phase baseline`. Sourced Mutation requires BindingFile and never downgrades to ordinary.

V-7 isolated live turn: copy `card0490-live.example.json` or let `scripts/verify-phone-home-grok.ps1` allocate ports into `.antiphon/card0490-live.json`. Grok credentials are a throwaway `GROK_HOME` directory (must contain `auth.json`) mounted at `/state/grok` via `PHONE_HOME_GROK_HOME`. Do not put tokens in the JSON file.
