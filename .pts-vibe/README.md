# `.pts-vibe/`

Per-repo configuration + state for the `pts-vibe-coding` plugin. Created by
`/pts-vibe-init`.

| File | Committed? | Purpose |
|---|---|---|
| `config.yml` | Yes | Per-repo configuration. Feature root, memsearch settings, hooks, review defaults. Edit by hand or via `/pts-vibe-init`. |
| `state.json` | No (gitignored) | Per-developer routing state: active feature, active epic, last command, and memsearch status. |
| `implementation.json` | No (gitignored) | Active implementation task capsule and per-epic flow metrics. Updated by `/pts-vibe-implement`. |
| `memsearch/` | No (gitignored) | Local Milvus Lite DB if `memsearch.scope` is `"repo"`. Binary, rebuildable, do not commit. |

## Why split config from state

`config.yml` is the same for every developer on the team — it's the contract.
`state.json` and `implementation.json` describe local work in progress —
different for each developer, branch, and machine. Merging those files would
be meaningless.

## Resetting state

```bash
rm .pts-vibe/state.json
# next /pts-vibe-* command recreates it from the template
```

## Resetting the memsearch index

```bash
rm -rf .pts-vibe/memsearch/
memsearch index docs/features/   # rebuild
```
