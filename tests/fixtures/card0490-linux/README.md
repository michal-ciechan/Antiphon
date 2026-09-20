# CARD-0490 native PC assets

Source-free guest disk, QEMU bundle, SDK 10.0.204, net9 runtime and offline NuGet inputs are pinned in `assets.lock.json`. FAT is transport only; product tests build on guest ext4.

Ordinary Code uses `scripts/test-card0490-native.ps1 -Ordinary -Phase baseline`. Sourced Mutation requires BindingFile and never downgrades to ordinary.
