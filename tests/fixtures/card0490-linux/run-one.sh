#!/bin/sh
set -eu
# Literal allow-list; the host wrapper supplies the filter.
exec dotnet run --project tests/Antiphon.PtyHost.Tests --no-restore --property:OutputPath=bin-card0490-native/ -- "$@"
