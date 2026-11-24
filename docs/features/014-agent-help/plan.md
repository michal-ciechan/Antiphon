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
  _common.md                    # authentication, conventions, errors; included by every file
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
- **Short-lived by default** (8 hours from the help drawer), with longer ones made on a Keys page in
  the user menu. Listed, named and revocable there. The key is shown once; only its hash is stored.
- Every request made with a key is recorded with the key's name and marked "via agent" in history,
  audit and the PR attribution, next to the person's name.
- **Kept out of reach of a key:** making or widening keys, sign-in and identity settings, secrets,
  trust adjustments, and deciding a human approval gate. A gate decision by an agent key is refused
  unless the instance turns that on.
- Rate limited, and an experiment started by a key follows the same budget rules as one started by
  hand.

The copied text states the key's scopes and expiry, and tells the agent to keep the key in an
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
| **1. Frame** | The Help drawer and button in the layout; the file format, `_common.md`, the placeholder filler and the two endpoints; the lint test; help files for Reviews (run, node, finding) and the review queue, working with the existing sign-in for reads | The lint test; a client test that the drawer fills ids from the route; one file's examples run in an API test |
| **2. Keys** | Personal agent keys: scopes, expiry, the Keys page, "via agent" in audit, **Include a key** in the drawer | API tests: a key cannot exceed its scopes or its owner; a revoked or expired key is refused; the key is never returned after creation |
| **3. Experiments** | Help files for suite, variant, experiment and case, written with that feature (experiments plan, phase 4), including "customise a variant" and "run a suite" | The examples run against the scripted server |
| **4. The rest of the site** | Files for boards and cards, agents and sessions, hosts and settings; the route-coverage test turned on | The coverage test |

## Decisions for the operator

1. **Key lifetime from the drawer:** 8 hours as planned, or shorter?
2. **Gate decisions by an agent key:** refused by default, as planned?
3. **Where the files live:** `docs/agent-help/` as planned (one place, easy to lint), or beside each
   feature's client code?
