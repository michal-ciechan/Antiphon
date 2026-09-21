#!/bin/sh
# Linux qualification wrapper: raw tty, then the published FakeGrok apphost.
if [ -t 0 ]; then
  stty raw -echo min 1 time 0 || true
fi
exec /opt/antiphon-tests/fakegrok/fakegrok "$@"
