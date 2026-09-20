#!/bin/sh
set -eu
case "${1:-}" in
  unlisted-method|changed-digest|unqualified-toolchain|non-ext4|prior-workspace|seeded-output)
    echo "probe-case $1"
    ;;
  *)
    echo "unlisted fixture probe"
    exit 2
    ;;
esac
