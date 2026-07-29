# UI screenshot testing & the backend-contract fixture chain

Three layers keep the UI documented, visually regression-guarded, and honest about what the
backend actually returns. **A story may only mock data that a backend integration test has
snapshotted** — that's the whole trick.

```
ContractSnapshotTests (tests/Antiphon.E2E)          ← REAL app: shared WebApplicationFactory/Kestrel + Postgres container
        │  captures + drift-guards scrubbed JSON
        ▼
client/src/test/fixtures/contract/*.json            ← the ONLY allowed source for story mock data
        │  imported + seeded into a QueryClient (no MSW — repo convention)
        ▼
*.stories.tsx (Storybook, port 17283)               ← component/page states, no volatile chrome
        │  rendered headlessly via the story iframe
        ▼
docs/ui-screenshots/*.png (+ README index)          ← visual docs + drastic-breakage tripwire
```

## 1. Contract snapshots (`ContractSnapshotTests`)

- Uses **`SharedApp`** — one `AntiphonAppFixture` (Kestrel + Postgres testcontainer) per test
  session, because factory boot is the expensive part. New E2E tests that just need "a running
  app" should take `await SharedApp.GetAsync()` instead of constructing their own fixture
  (tolerate shared DB state: unique names/ids per test). Tests needing special flags
  (prebuilt frontend, mock executor) still construct their own.
- Each test drives a **deterministic scenario** through the real API (plus DB seeding for things
  only live sessions produce, e.g. transcript entries), then snapshots responses into
  `client/src/test/fixtures/contract/`.
- Scrubbing keeps snapshots stable: GUIDs → sequential placeholders, timestamps → a fixed
  instant, temp workspace paths → `<workspace>`. Content hashes/sizes stay real because the
  scenario content is fixed.
- **First run captures; later runs compare.** A drifted backend response fails the test. If the
  change is intentional: check every story consuming the fixture against the new shape, delete
  the fixture file, re-run to re-capture, commit both.

## 2. Stories seeded from fixtures

- Repo convention (see `DirectoryAutocomplete.stories.tsx`, `FilesReviewPanel.stories.tsx`):
  no MSW in Storybook — each story wraps the component in a `QueryClientProvider` whose cache is
  **pre-seeded from the contract fixture imports**, with `retry: false`, `staleTime: Infinity`,
  `refetchInterval: false`. Rendering is deterministic and network-free.
- Component stories contain **no volatile chrome** (navbar, live status badges, clocks) — that's
  what makes the screenshots comparable run to run. Page-level stories should mask or omit those
  regions.
- Components can expose small storybook-only hooks (e.g. `FilesReviewPanel`'s
  `initialSelectedPath`) to reach interesting states without play functions.

## 3. Screenshots (`npm run screenshots`)

- `client/scripts/storybook-screenshots.mjs` connects **over CDP to the already-running Edge**
  (`BU_CDP_URL`, default `localhost:9222` — the same browser browser-harness drives; no browser
  download), renders every story's bare `iframe.html` (manager chrome never appears), disables
  animations/caret, waits for Monaco where present, and writes
  `docs/ui-screenshots/<story-id>.png` plus a `README.md` index.
- Uses the running Storybook on 17283 or boots one itself (`--ci`) and kills it after.
- Filter by story id substring: `npm run screenshots -- filesreviewpanel`.
- **Workflow**: after UI changes, regenerate and review the image diff in git — a drastically
  broken render is visible before anyone opens the app, and the committed PNGs double as UI
  documentation.

## Adding coverage for a new component

1. Add/extend a scenario in `ContractSnapshotTests` for the endpoints it consumes; run to capture
   fixtures.
2. Write stories that seed a QueryClient from those fixture imports (never hand-written shapes).
3. `npm run screenshots -- <story-id>` and commit the PNGs.
