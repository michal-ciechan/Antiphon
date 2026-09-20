#!/bin/sh
set -eu
# Immutable outer driver for guest probes. Do not mutate this file in an H-PC.
exec /bin/sh /work/card0490/tests/fixtures/card0490-linux/guest-init.sh "$1"
