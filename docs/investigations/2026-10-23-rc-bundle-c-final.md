# Final rc bundle C recipe (verification in progress)

Selection master: `50e71d63ef185ba11081e7f868d2e974072e6733`. Base: `0714a9a89e4bff65aae7e0b4b94098694aeb931c`.

All 21 new reviewed/master pairs match stable patch IDs. Conditional CARD-1483 S3,
CARD-1512 and CARD-1486 S4/repair are absent from this master snapshot. CARD-1495
and CARD-0167 stay excluded. CARD-1232 contributes only its exact slow-test allowance
as a path-only prerequisite to CARD-1519; no implementation is ported.

The complete script below is staged before the dry run. Builds and the six requested
checkpoint selections are pending; no green verification is claimed yet.

`rc-bundle-c-final.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
BASE=0714a9a89e4bff65aae7e0b4b94098694aeb931c
test "$(git rev-parse HEAD)" = "$BASE" || { echo 'STOP: wrong base'; exit 1; }
test -z "$(git status --porcelain)" || { echo 'STOP: dirty worktree'; exit 1; }
# Freeze the selection, but prove every source is still reachable from fetched master.
SELECTION_MASTER=50e71d63ef185ba11081e7f868d2e974072e6733
git fetch origin master
git merge-base --is-ancestor "$SELECTION_MASTER" origin/master
assert_source() {
  git merge-base --is-ancestor "$1" "$SELECTION_MASTER" || { echo "STOP: source outside frozen master: $1"; exit 1; }
  git merge-base --is-ancestor "$1" origin/master || { echo "STOP: source no longer on master: $1"; exit 1; }
}
undo() {
  local base=$1 p; shift
  for p in "$@"; do
    if git cat-file -e "$base:$p" 2>/dev/null; then git restore --source="$base" --staged --worktree -- "$p"; else git rm -q -f --ignore-unmatch -- "$p"; fi
  done
}
pick() {
  local s=$1 base bad p
  assert_source "$s"
  local -a drop=()
  while IFS= read -r p; do
    case "$p" in docs/superpowers/plans/*|docs/superpowers/mutations/*|.antiphon/task-*.md) drop+=("$p");; esac
  done < <(git diff-tree --no-commit-id --name-only -r "$s")
  base=$(git rev-parse HEAD)
  if git cherry-pick -x "$s"; then
    if [ "${#drop[@]}" -gt 0 ]; then
      undo "$base" "${drop[@]}"
      if ! git diff --cached --quiet; then git commit -q -m "docs(rc): drop unconsumed plan changes from $s"; fi
    fi
    echo "PICK clean $s"
  else
    bad=$(git diff --name-only --diff-filter=U | grep -v -F -x -f <(printf '%s\n' "${drop[@]}") || true)
    if [ -n "$bad" ]; then echo "STOP $s unexpected conflict: $bad"; exit 1; fi
    undo "$base" "${drop[@]}"
    git -c core.editor=true cherry-pick --continue
    echo "PICK plan-conflict-dropped $s"
  fi
}
# Preserve the full dependency order while omitting unconsumed plan/report-only commits.
skip_docs() {
  local s=$1 p
  assert_source "$s"
  while IFS= read -r p; do
    case "$p" in docs/superpowers/plans/*|docs/superpowers/mutations/*|.antiphon/task-*.md) ;;
      *) echo "STOP $s is not plan/report-only: $p"; exit 1;;
    esac
  done < <(git diff-tree --no-commit-id --name-only -r "$s")
  echo "SKIP plan/report-only $s"
}
# CARD-1499
pick 3d921474345ff25c3af2fdecd8f94b3fa0bdc170
pick 153d9a0683d5b18d587f82f7f6bfcae126ed70a6
pick 3c9db61871cb48f392da59b7ec002651a90a655e
# CARD-1494
pick e6032f85b01d5807cf538f8dbd1ee0813a3ad534
pick f6af11f29d7eb89b74486a766b199d03081e0607
pick 10240653d041ffa00cdc1ed52d12bdf909b5a20e
pick 25c3d343a78dd1afef510b084702cb88f6001526
pick a01b38a2c816a9af7ce3c2872346a6b8685b331d
# CARD-1493 S1 + repair
pick 8866e446762f3fcb7309ab5e25d3824406a637d7
pick f2cfe512d9e3df7d92c55cb62d3cbe09299ffd4f
pick 951e5e210a7f0342557acc436c4ae49ea2116943
pick 4800c50f3216ca5399d900a204004d8eea64ea98
# CARD-1448
pick 0498c748a448f367e6153316c992585d36dea74b
pick a23ca2869618811619bcb0965d386ecb6e786c18
# CARD-1448's third/fourth commits are plan/report-only; preserve their position.
skip_docs e4ef4c621cb43835254db8d9d21fb79abc18f7bd
skip_docs 222ceeb0074427fb42b7a7cb8b439fa0e17ffa59
# CARD-1502: all eight re-cut commits, now using their landed master copies.
pick a04e42f5a137d7eac8fcd4d9e1c9f63abcf89518
pick c2c837bc6ff8ff61dfd966252f38ef6486037782
pick df23c2778e2c8468dc5069a83fd6cd52b71339e9
pick c2c739e1650cfa518b2214cfd9eb7c146ce4e8a9
pick 8b47b6c0883675c46b694cafcffc80f4321eab10
pick 979876a637a3546742b9cd9fefbcef0b14d9f8a0
skip_docs 4ec059d7befb21db0b2f9f53dce35f5f4e87397c
skip_docs 065f9c203ce823e7d0ff1f21970bed3eaeba7236
# Final re-cut report identifies the landed owner; no runtime delta.
skip_docs e6bfcbc2c13f4a7431b1d314e2aef87f30de4ca7
# CARD-1486 S1 S2 S3
pick 27e994af1f6e8de1493408e7a0ec25404199ac0d
pick 8b7edcdde449f7e85e39e27b0f166ebaf92a5b3e
pick 645c7b30b884e3b17352ca4a2dc5c363d09c6c27
pick bcd04d29e8aec21988b281c6b0dc90d21d10a9d3
pick 9516bfd35a3a3e2a075ba71fa4e3c586d7104220
pick 12a2141e6047bc1b40b4f4d74815293f1ef9a7ec
pick 0fc7a4c2b08e6df7354cff2f374c9d603ab5ac03
pick 2316cd6faf3852ad54eb32b77d3a7eae75d910f3
# CARD-1496
pick ccac6053ea4dac9af114361f9de7f955838f3998
pick 0c4386050774c04d2031fd6afd62a3790ea108b6
pick 9bcbb9cbb704239bbb567cdcdb2d0d896a6bd7a6
# CARD-1506 (manifest generated separately)
pick 87262adf4ee92a4eefe8bc0405da3f5775835204
pick c15961b94b3ddef659af9fb9c32175422faf5e5c
pick 4db0d927a9ca1376f8f4a60d94a5b855ce769d36
# Regenerate the CARD-1506-only rc definition inventory before CARD-1483 adds a project.
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check
[ "$(cat server/Application/Services/LandVerificationBuildGraph.manifest.txt)" = "$(printf '%s\n%s' A1EBB3D0B4C0DF9BB31E417D0AFD7607CDC4BD5A404DACE986CC7CD8EC274DD6 57)" ] || { echo 'STOP: intermediate manifest drift'; exit 1; }
git add server/Application/Services/LandVerificationBuildGraph.manifest.txt
git commit -q -m 'build(rc): regenerate CARD-1506 manifest on rc'
# CARD-1404
pick 3d8650eee291b6cef217e7f3367956b45f58cfdb
pick 7370f414ba723a6d41c1ee561f546871cda80503
pick ffa98a8f76588c12df6b0437d24b541762b95f09
# CARD-0266
pick 138c72bdad4fb52b7d4903c91a18f013c7d7b384
pick 96ee5df43209cba8d991ac5e8d11c2d65f90b6d1
pick 60da72847203962d99638fbb4630cdba719bc78b
pick 5f560e91aefb21ffdcd65459b213cf6267ebc3ff
pick 86d03f3fdbcd2f30bec6e73ac0c15dfc093ec101
pick 52debc786ac54e321d0f7b6c2c75e342f468cf34
# CARD-1483 S1 + all landed repairs
pick 7046601e73818ae729fc843e7e6d6b3fbb54d6db
pick 52c2f689f42c1e9b9fd9c90fa5aa3366418dcaaf
pick 19e6c73957bc01e36f2b0b85229e6f0c0ed5e76c
pick a0649997a9b8ef6425bcf327bc98574828f23bdf
pick 111570541259d84f99f41a22bd5b46dff84d13a1
pick 62c61a29fe2797ac3e32bd195a976a2e62382217
pick 296f74948994ace98e0832dd21f10588c8b0eaef
pick 8429109ed37cb4361deba6cbd26054ec27414317
pick 2132c24d3d1bb9ed67eaffd558cf2766a5bb95c7
pick c3d806fb88923dfa798e4a5a91df00650fe07ad9
pick 39c14c776e0c0e357a5f57f9c1c85085f65b35eb
pick b20e12e2f4f2878f25280a1576bffae59659fb57

# CARD-1483 S2: five code/test commits and the minimal-verification docs tip.
pick df8c6695a13f95a260f74199109f25de05466df8
pick 17dc4a9899cab08323774655c4d9a86f226219dd
pick dd980c82e9932b35bf4bf2d9d6f5726380709e0b
pick 285aa79bf05fc677d437dc4185be71571321d9ab
pick aed1449a8a7a6c0321ae81b287fa1066a222ad0e
pick 8ef0baca812807bf83c7c34578173ac61a36eed6

# CARD-1500: Review order, including explicit prerequisite/scope-plan skips.
skip_docs 5d9cdbe9b92a0a8ab5fa94588a8af94d8afdb0f4
pick 82083a4043c1cf41b863d1aafadf8e16e6dc5a9a
pick c4af6e8c7f679fe2327401404d2e81f7277700e4
pick 9c4143a0dd980b54f0b2916578176553dac5921a
pick f6cf651292a64ba4f7e7edfd9b845697503b8963
pick 9223cac4fd5d3b4a545c2c806ad9db48587a0dd9
pick 565ce3cb987314326392aa0caa99dd4eb79a465d
pick 5ffed3091a204c08f87421693d9bbb43e1692f82

# CARD-1519 / CARD-1457: retain CARD-1232's exact missing allowlist context.
# Only this path from the prerequisite is ported; no CARD-1232 runtime/test code.
ALLOWLIST_SOURCE=ba3f39731d9a99ea10717d3b4a9d795ba74f8d27
assert_source "$ALLOWLIST_SOURCE"
git diff "$ALLOWLIST_SOURCE^" "$ALLOWLIST_SOURCE" -- tests/Antiphon.Tests/slow-tests-allowlist.txt | git apply --index
git commit -q -m "test(rc): carry CARD-1232 slow allowance required by CARD-1519" -m "Path-only prerequisite from $ALLOWLIST_SOURCE; no runtime or test implementation port."
pick 6d1fdc8f0fa238a4d2b4d06ef9432b831f85ac52
pick e72e819ba693928e829d0a3e2719cf9e16f37a77
pick 6b80707b3a9a54c0b77775802496b8034d2930ba
pick 339a851f82a2342d2ed54e5ddaaaab4b75c440d5
pick f974ac3c420f17df00d6adace5bd2a1410444153
pick 08533c587550e6a89013f437b60e543ebf4151ca
pick 19a0b6aa27c9cd925ddb045d5de1e6be364f42e2

# CARD-1511: skip diagnostic plan, then keep the fixture repair, drop plan/candidate.
skip_docs 522cbb93de9073c3e889c21933f173494eb6a3de
pick 745cc90e6d5c838bee86ff9c6a133225641465a6

# EXCLUDED: CARD-1495 and CARD-0167, by caller decision.
# PENDING at SELECTION_MASTER: CARD-1483 S3; CARD-1512;
# CARD-1486 S4 plus its test repair. Do not infer permission to append branch tips.

# Final inventory includes CARD-1483 S1/S2 and every selected addition.
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check
git add server/Application/Services/LandVerificationBuildGraph.manifest.txt
git commit -q -m 'build(rc): regenerate final bundle C land graph'
cat server/Application/Services/LandVerificationBuildGraph.manifest.txt
echo "bundle tip $(git rev-parse HEAD) tree $(git rev-parse HEAD^{tree})"
```
