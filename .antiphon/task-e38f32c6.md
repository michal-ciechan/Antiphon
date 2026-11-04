# Review e38f32c6: CARD-1693 r4 (Debug 47f9be84), round 1

Subject: feat/card-task-47f9be84, tested SHA 3d2d5261aab5e1a2759f929d0ce37c17820e0abf, base e214f1d1c8db33120d4abd5dd68e23a3d48ce226.

## Verdict

The production change in `scripts/c1008-volume-metadata.sh` is sound. Across 87 scratch fixtures I found no real credential and no inner-docker data that the new rules would let a retire delete. Each accepted shape is the observed one.

There is one test-adequacy defect: D-1. A mutant named in the brief survives.

## Runs (one build lease, native output, UseAppHost=false, TEMP=/tmp)

- Build of Antiphon.Tests: 0 errors.
- VolumeRetirementContentTests + HostJqFixtureClosureTests, one lease: **34/34 passed**, 0 failed, 0 skipped, 16m08s.
- C1210_Roster_closure_matches_source_gates + C849_Docker_volume_paths_have_no_bare_host_reads: **2/2 passed**, 18.8s.
- The five earlier tests pass inside the 34 (item 3).
- check-evidence-diff over e214f1d1c..3d2d5261a: 3 commits, 0 violations.
- `git merge-tree --write-tree origin/master HEAD` against 3749c84334afb2fadc8ad345402db9fad79e11a8: clean (tree 20f57b74c).

## Scratch fixtures (not committed)

These drove the real `c1609_scan_state` and `c1609_scan_dind` with a sudo shim and a `c1008_volume` stub.

### State volume: 46 cases, all as intended

**Refused with `state-credential`:**
- A session key hardlinked to another valid key name (links=2).
- Sizes 257 and 10M (sparse).
- Pid with a leading zero, 11 digits, non-digit or negative.
- One uppercase hex digit, 63 hex, or 65 hex.
- An embedded newline before `.key`.
- `claude/sessions/sub/`, `claude/Sessions/`, `other/claude/sessions/`, `sessions/` at the root, or `claude/` directly.
- A `claude` or `sessions` symlink to a directory inside the volume (the hit surfaces at its real path and refuses).
- A FIFO, a socket, a symlink or a directory with the exact key name.
- `auth.json`, `.credentials.json`, `credentials.json`, `id_ed25519`, `.pem`, `.p12`, `.pfx` and `.key.pem` under `claude/sessions/`.
- A leading space in the name.
- A valid key plus one invalid hit.

**Accepted:**
- Sizes 0, 111 and 256.
- A 10-digit pid.
- A sparse file with an apparent size of 200.
- 23 valid keys together.

### dind volume: 41 cases, all as intended

**Refused with `dind-unknown`:**
- A `.key` file inside volumes/<hex>/_data.
- In `_data`: a file, a hidden file, an empty file or a nested directory.
- `opts.json` or another directory beside `_data`.
- backingFsBlockDev as a non-empty file, a 1-byte sparse file, a directory or a FIFO.
- engine-id of 35 or 37 bytes, or as a symlink, a directory or a FIFO.
- containers/ holding one directory or one file, or containers as a symlink.
- metadata.db as a FIFO or a symlink.
- An inner volume name with one uppercase hex digit, 63 hex, 65 hex, a plain name or a trailing newline. Also `_data` with a newline.
- A volume symlink or a `_data` symlink.
- A file at the top of volumes/, volumes/ as a symlink, or an unknown top-level entry.
- A real tmpfs mount under volumes/<hex>/_data holding a file (made with `unshare -rm`), and a mount at volumes/ with a directory in it.

**Refused with `dind-unreadable` (fails closed):** an unreadable `_data`.

**Accepted:**
- The baseline layout.
- A `b` backingFsBlockDev (fed straight to `c1609_dind_bookkeeping`; a `c` device, another name, or a nested `b` refuse).
- A volume without `_data`, and two empty volumes.
- A 1G sparse metadata.db.
- An empty tmpfs mount at `_data`.

## Digest binding (item 2)

- `c1609_inventory_digest` filters `^disposable-fixture` lines, then `LC_ALL=C sort | sha256sum`, so the order is stable.
- Preview appends the `c1609_role_metadata` output to preview-audit.out (c590-remote.sh:9289-9292).
- Apply writes the same output first into apply-audit.out (c590-remote.sh:8595). That file is `C1609_APPLY_INVENTORY_FILE`, compared at c1008-retirement.sh:632.
- Mutant M4b proves the binding (below).

## Mutants

Each was an exact-path edit, run as `Claude_session_keys_are_disposable_state_bound_into_the_inventory` alone in its own lease, then restored with `git checkout -- <path>`. The tree was clean after each.

| Mutant | Edit | Result |
|---|---|---|
| M1 | pid `[1-9][0-9]{0,9}` → `[1-9][0-9]*` (widen) | **SURVIVED**: 1/1 passed |
| M2 | whole path regex → `\.key$` (any *.key) | killed: 1 failed at `2.<UPPER>.key refuses` |
| M4 | session-key line prefix renamed (not printed as disposable-fixture) | killed, but by the `okText.ShouldContain` print assertion, not the drift check |
| M4b | digest awk excludes `classifier=claude-session-key-v1`; the line is still printed (c1008-retirement.sh) | killed by `a changed session-key set invalidates the Ready preview`. Unbound, apply reached `state=Applying`, so the digest is the only guard and it is tested |

- I did not re-run the size-limit or hardlink mutants. Debug's `size` (257) and `hardlink` variants cover them, and my fixtures confirm both shapes refuse.
- A literal `+` mutant (`[1-9][0-9]+`) would be killed by the accepted `1.<hash>.key` row.

## Defects

### D-1 (test adequacy; no safety impact)

- **Where:** `tests/Antiphon.Tests/Scripts/VolumeRetirementContentTests.cs`, the `variants` array in `Claude_session_keys_are_disposable_state_bound_into_the_inventory`.
- **Failure:** widening the pid bound `{0,9}` to `*` in `c1609_state_session_key` leaves the test green (M1, 1/1 passed). The brief requires each named mutant to turn a test red.
- **Why:** no variant has a pid longer than 10 digits.
- **Fix:** add a refusing variant `("file", Path.Combine("claude", "sessions", "12345678901." + SessionHash + ".key"))`. Optionally also add an accepted 10-digit key (`1234567890.`) to the ok set to pin the upper bound from both sides. This is test-only; the script needs no change.

## Advisories

- **A-1:** volumes/metadata.db is accepted at any size and content (type `f` only). The digest binds its presence, not its size. This is acceptable for docker's volume-metadata bolt db (observed size 32768).
- **A-2:** these cases are not scanned, and they behave the same at base e214f1d1c:
  - names outside the credential globs, such as a trailing newline after `.key` or `.KEY`;
  - a `claude` symlink pointing outside the volume, which `find -P` does not follow, and which a volume delete would not follow either.
- **A-3:** an empty filesystem mounted at volumes/<hex>/_data is accepted. It holds no data.

## Cleanup

- Deleted every bin/ and obj/ directory this build produced, using literal paths.
- Deleted the scratch fixtures fx..fx5.
- No host, volume, retire-temp or deploy was touched, and no key file contents were read.
