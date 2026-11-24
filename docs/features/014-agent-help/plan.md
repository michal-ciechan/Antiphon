# Agent help: "copy for your agent" on every page (plan)

Status: plan, 2026-11-24. Nothing here is built yet.

## Goal

On any page of Antiphon a person can click **Help**, then **Copy for agent**, and paste the result
into an agent running on their own machine (Claude Code, Codex, any agent with a shell). That agent
can then do what the page does, on the person's behalf: read, customise, run actions and call the
endpoints. The Experiments area ([plan](../013-experiments/plan.md)) is the first user: "customise
this variant" and "run this suite" must be doable by an agent, not only by clicking.

The help text is **Markdown files with placeholders**, kept in the repo next to the feature they
describe. The site fills the placeholders (base URL, ids on the page, a key) when it shows or copies
them.

## What the person sees

- A **Help** button in the page header on every page, and on panels that have their own actions (a
  graph node panel, a variant in the workflow editor, a finding, a gate).
- The help drawer has two tabs:
  - **For you:** how the page works, in plain words.
  - **For your agent:** the text an agent needs, with the placeholders already filled for this
    page, and a **Copy** button.
- Options beside Copy:
  - **Include a key** (off by default). On: the site makes a short-lived key for this person, scoped
    to this area, and puts it in the copied text. Off: the text keeps the placeholder and says which
    environment variable to set.
  - **Scope:** this item only (this variant, this run), or the whole area.
  - **Read only** or **may change things**.
- A site-wide entry, **Help → Agent guide**, gives the general text: how to authenticate, the
  conventions, and the list of areas with a link to each area's file.

## The help files

One file per page or panel, under `docs/agent-help/`, served by the site and readable in the repo:

```
docs/agent-help/
  README.md                     # for people: what this folder is, how to point an agent at it, how to add a file
  index.md                      # the top index: every area, and how to use this folder
  placeholders.md               # every placeholder: meaning, where its value comes from, an example
  _common.md                    # authentication, conventions, errors; included by every file
  experiments/index.md          # one index per area: its pages, actions and files
  experiments/suite.md
  experiments/variant.md
  experiments/experiment.md
  experiments/case.md
  reviews/run.md
  reviews/node.md
  reviews/finding.md
  review-queue/gate.md
  boards/card.md
  agents/session.md
  …
```

Each file has a small header and fixed sections, so agents and people find the same things in the
same place:

```markdown
---
title: Customise a variant
area: experiments
route: /experiments/variants/:variantId
scopes: [experiments.read, experiments.write, experiments.run]
placeholders: [BASE_URL, API_KEY, VARIANT_ID, SUITE_ID, WORKFLOW_REVISION_ID]
---

# Customise a variant

You are acting for {{USER_LOGIN}} on the Antiphon instance at {{BASE_URL}}.
Send `Authorization: Bearer {{API_KEY}}` with every request.

## What this is
A variant is a graph setup: the workflow graph plus each step's model, prompt and settings. …

## What you can do
| Action | Request |
|---|---|
| Read this variant | `GET {{BASE_URL}}/api/eval/variants/{{VARIANT_ID}}` |
| Change a step's settings | `PATCH {{BASE_URL}}/api/eval/variants/{{VARIANT_ID}}/steps/<stepId>` |
| Change the graph | `PUT {{BASE_URL}}/api/eval/variants/{{VARIANT_ID}}/graph` (validate first) |
| Validate | `POST {{BASE_URL}}/api/review-workflows/validate` |
| Save as a new variant | `POST {{BASE_URL}}/api/eval/variants` |
| Run suite {{SUITE_ID}} with it | `POST {{BASE_URL}}/api/eval/experiments` |

## Examples
(one worked request and response per action)

## Rules
- Saving always makes a new variant. You cannot change the live workflow from here.
- An experiment pauses when its budget is reached. Report that; do not raise the budget unless asked.
- Before anything that cannot be undone, say what you are about to do and wait for a yes.

## When something fails
(the error shapes for this area and what each means)
```

**Indexes, so someone can point an agent at the folder**

Decided 2026-11-24. The folder explains itself, so a person can give an agent just its address (the
repo path, or `{{BASE_URL}}/api/agent-help`) and the agent can find its own way:

- `index.md` says what the folder is, how to read a help file, how to fill placeholders, and lists
  every area with one line each and a link to its index.
- Each area's `index.md` lists its pages and panels, the actions each file covers, the scopes
  needed, and the placeholders each file uses.
- `placeholders.md` is the reference for every placeholder: what it means, where a person or agent
  gets the value (which page shows it, which request returns it), its format, and an example.
- `README.md` in the folder is the entry for people: what the folder is for, the one line to give
  an agent ("read `docs/agent-help/index.md`", or the URL), and how to add or change a help file.
- **The repository's main `README.md` links to it** in its own short section ("Working with
  Antiphon through an agent"), and `AGENTS.md` gets a row in its index table, so both people and
  agents arriving at the repo find it.
- The indexes are **generated from the files' headers** and checked in, so they cannot drift; the
  lint test fails when they are out of date.
- **They can be queried:**
  - `GET /api/agent-help` returns the index as JSON or Markdown;
  - `GET /api/agent-help?q=<words>` searches titles, actions and routes ("approve a gate", "run a
    suite");
  - `GET /api/agent-help?route=/experiments/variants/123` returns the files for a page address;
  - `GET /api/agent-help?placeholder=VARIANT_ID` returns what the placeholder is and which files
    use it;
  - `GET /api/agent-help/placeholders` returns the placeholder reference.
- Reading the indexes and raw files needs no key. Filled text for a person's own items needs their
  sign-in or key.

**Placeholders**

| Placeholder | Filled from |
|---|---|
| `{{BASE_URL}}` | The address the person is using the site at |
| `{{API_KEY}}` | A new key when **Include a key** is on; otherwise left as is, with a note to set `ANTIPHON_API_KEY` |
| `{{USER_LOGIN}}` | The signed-in person |
| `{{KEY_EXPIRES_AT}}`, `{{KEY_SCOPES}}` | The key that was made |
| Ids: `{{RUN_ID}}`, `{{NODE_ID}}`, `{{VARIANT_ID}}`, `{{SUITE_ID}}`, `{{CASE_ID}}`, `{{CARD_ID}}`, `{{BOARD_ID}}`, `{{PROJECT_ID}}`, … | The route and the selected item |
| `{{SERVER_VERSION}}` | `GET /api/version`, so the agent can tell when the text is stale |

- A file declares the placeholders it uses. A page that cannot fill one leaves it visible and lists
  it under "you still need to supply".
- The same files are served raw, with placeholders unfilled, at
  `GET /api/agent-help/{area}/{name}.md`, and filled at `GET /api/agent-help/{area}/{name}?…ids`.
  An agent that already has a key can fetch other areas' help itself; `_common.md` tells it how.
- `GET /api/agent-help` lists every file with its title, route and scopes.

## Keys: acting on the person's behalf

Today there are Delegation Capabilities (a named token for dispatching tasks) and browser sign-in.
Neither fits "an agent on my machine does what I can do on this page". This adds **personal agent
keys**:

- Made by a signed-in person, for themselves. A key acts **as that person** and can never do more
  than they can.
- **Scoped** to areas and to read, write or run (`experiments.run`, `reviews.write`, …), and
  optionally to one item.
- **Lifetime by default** (decided 2026-11-24): a key does not expire unless its owner revokes it.
  The default lifetime is an instance setting (`AgentKeys:DefaultLifetime`: none, or a duration),
  with an optional maximum an instance can enforce, and a person can pick a shorter one when making
  a key. Keys are listed, named and revocable on a Keys page in the user menu, with when each was
  last used. The key is shown once; only its hash is stored.
- Every request made with a key is recorded with the key's name and marked "via agent" in history,
  audit and the PR attribution, next to the person's name.
- **A key can approve** (decided 2026-11-24): gate decisions, approvals and dismissals are allowed
  with the `….approve` scope, as the person. They are recorded "via agent".
- **The help text tells the agent not to approve on its own.** `_common.md` and every file with an
  approving action carry this rule: *do not approve, decide a gate, dismiss a blocker or merge
  unless the person asked for that action or gave you standing permission for it; when unsure, say
  what you would approve and why, and wait.* The API also returns a `hint` on approving endpoints
  repeating it.
- **Kept out of reach of a key:** making or widening keys, sign-in and identity settings, secrets,
  and trust adjustments.
- Rate limited, and an experiment started by a key follows the same budget rules as one started by
  hand.

The copied text states the key's scopes and expiry (or that it has none), and tells the agent to keep the key in an
environment variable and never write it to a file, a commit or its output.

## Keeping the files true

- **A test reads every help file** and checks that each route it names exists, that each declared
  placeholder is used and each used one is declared, and that every `scopes` entry is a real scope.
- **A test walks the routes** and fails when a page with actions has no help file. A page may opt
  out with a stated reason.
- The definition of done for a feature with a page gains one line: its help file exists and its
  examples run against the scripted test server.
- The examples in a file are run as tests where the area has a scripted server (the review engine
  and experiments do).

## Phases

| Phase | Delivers | Checked by |
|---|---|---|
| **1. Frame** | The Help drawer and button in the layout; the file format, `_common.md`, the folder's `README.md`, the generated indexes and `placeholders.md`, the links from the main `README.md` and `AGENTS.md`, the placeholder filler, and the endpoints with search; the lint test; help files for Reviews (run, node, finding) and the review queue, working with the existing sign-in for reads | The lint test; a client test that the drawer fills ids from the route; one file's examples run in an API test |
| **2. Keys** | Personal agent keys: scopes, expiry, the Keys page, "via agent" in audit, **Include a key** in the drawer | API tests: a key cannot exceed its scopes or its owner; a revoked key is refused, and an expired one when a lifetime is set; an approval needs the approve scope; the key is never returned after creation |
| **3. Experiments** | Help files for suite, variant, experiment and case, written with that feature (experiments plan, phase 4), including "customise a variant" and "run a suite" | The examples run against the scripted server |
| **4. The rest of the site** | Files for boards and cards, agents and sessions, hosts and settings; the route-coverage test turned on | The coverage test |

## Decisions for the operator

1. **Key lifetime:** decided 2026-11-24: lifetime by default, configurable in settings.
2. **Approvals by an agent key:** decided 2026-11-24: allowed, with a rule in the help text not to
   approve unless asked or given permission.
3. **Where the files live:** decided 2026-11-24: one place, `docs/agent-help/`, with its `README.md`
   and `index.md`, linked from the main `README.md` and from `AGENTS.md`.
