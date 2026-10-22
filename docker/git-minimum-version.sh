#!/bin/sh
# CARD-1260: the one declaration of the Git landing floor (CARD-0661). LandingGit,
# LandOperationFactory, GuardedWorktreeRemoval and GuardedVerificationRemoval run
# `git show-ref --exists`, new in Git 2.43, to tell a missing ref (2) from a lookup error (1).
# The server image (root Dockerfile) and every runner target run this after installing Git, so
# the build fails below the floor. Raising the floor is a change here and in docs/docker-stack.md.
# Usage: git-minimum-version.sh [version]   (default: the installed `git --version`)
set -eu
GIT_MINIMUM_VERSION=2.43
if [ "$#" -gt 0 ]; then
  installed=$1
else
  installed=$(git --version 2>/dev/null | cut -d' ' -f3) || installed=
fi
case "$installed" in
  '' | *[!0-9.]* | .* | *. | *..*)
    echo "git-minimum-version: unparseable Git version '$installed'" >&2
    exit 1
    ;;
esac
if printf '%s\n%s\n' "$GIT_MINIMUM_VERSION" "$installed" | sort -V -C; then
  echo "git-minimum-version: Git $installed meets minimum $GIT_MINIMUM_VERSION"
else
  echo "git-minimum-version: Git $installed is below minimum $GIT_MINIMUM_VERSION" >&2
  exit 1
fi
