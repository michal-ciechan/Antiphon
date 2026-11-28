# Review engine: getting started

This guide is for anyone running their own Antiphon review instance: for one person's repos, a team
or a company. It covers what to install, which keys to set, how to deploy, what the gates do and how
to switch them, how to change the workflow, and where checks, prompts and models are configured.

[design.md](design.md) explains why the engine works as it does, and [api.md](api.md) is the HTTP
contract. This guide describes what the code does today. Anything that exists only in the design is
marked **not built yet**, and section 12 lists those items together.

## 1. What you get, and what is not built yet

Antiphon polls your GitHub repos. For each open PR it does the following:
- It picks the review checks that apply to the change.
- It runs AI reviewers in parallel, verifies their findings, and posts one PR review with inline comments.
- It reviews each new push incrementally.
- It approves a clean PR itself, or asks a person to when the change is risky.
- It keeps approved branches up to date with the base and merges them. Merging needs a person's
  confirmation unless the merge gate is switched off for the repo.

It reports progress through two commit statuses, `antiphon/review` and `antiphon/approval`. The UI's
run page shows each step, the findings, and the gates waiting on a person.

Read this table before you plan a rollout for more than one person:

| Area | What works today |
|---|---|
| Code hosts | GitHub: github.com, or GitHub Enterprise Server through `BaseUrl`. Not GitLab, Bitbucket or Azure DevOps. |
| Triggers | Polling only. There is no webhook receiver. |
| Users | Sign-in with GitHub (section 7.1), **off by default**. With it off, everyone who opens the UI acts as one viewer (`Review:DefaultViewerLogin`), so keep the UI on a private network. Turn it on only on a server that runs the review engine alone. |
| Teams | Not read from CODEOWNERS or GitHub teams. On real PRs gates have no team, so the Teams queue is empty and "assign at random" refuses. |
| Workflow | One built-in workflow, compiled into the server. A deployment or a repo cannot supply its own yet. |
| Per-repo tuning | A repo can add its own checks (`.antiphon/review/checks/`). A repo `policy.yaml`, repo prompts and a repo workflow are designed but not read yet. |
| Policy | Server configuration (`Review:Policy`), shared by every repo, apart from two per-repo lists: gates switched off, and repos whose authors may decide their own gates. |
| Docker | `docker-compose.review.yml` runs a review-only stack: the server with the Claude Code CLI, and Postgres. The server clones the repos itself (section 6.3). No image is published; compose builds it. |

## 2. What you need

- **PostgreSQL 16.** The server creates the database if it is missing and runs its migrations at
  startup. The review engine shares the Antiphon database; it needs no database of its own.
- **The server:**
  - from source: the .NET 10 SDK, plus Node 22 for the UI;
  - or the Docker image, which serves the built UI.
- **git** on the server's `PATH`.
- **A GitHub App** installed on the repos (section 3), or a personal access token.
- **Something to answer the model calls** (section 5). The simplest is the Claude Code CLI, signed in
  on the server machine. The alternatives are:
  - an Anthropic API key;
  - a Jev key;
  - any OpenAI-compatible endpoint;
  - an Antiphon session runner, which runs reviewers as delegated agent tasks.

## 3. GitHub: create the App

Use a GitHub App rather than a personal access token. The App posts as `<app-slug>[bot]` and can
approve PRs that you opened. A token approves as you, and GitHub does not let you approve your own PR.

1. Go to GitHub, then Settings (your account's or the organisation's), Developer settings, GitHub
   Apps, **New GitHub App**.
2. Name it, for example `acme-review`. The homepage URL can be your Antiphon URL. Under Webhook,
   **untick Active**: Antiphon polls, so it needs no webhook.
3. Under **Repository permissions**, set:

   | Permission | Access | Used for |
   |---|---|---|
   | Pull requests | Read and write | Reading PRs, diffs and files; posting reviews and inline comments; replying to and resolving threads; approving; PR conversation comments and reactions |
   | Contents | Read and write | Reading files, trees and comparisons. Write is needed to update branches and to merge. If you keep the merge gate on and never update branches, Read is enough. |
   | Commit statuses | Read and write | The `antiphon/review` and `antiphon/approval` statuses |
   | Metadata | Read | Required by GitHub |

   Antiphon's own app also has Checks: write, but the engine does not use check runs yet. No Issues
   permission is needed: PR conversation comments and reactions work with the Pull requests
   permission.
4. Create the App. Note its **Client ID** (`Iv23…`) or its numeric **App ID**; either works as
   `AppId` below.
5. Under Private keys, **generate a private key**. GitHub downloads a `.pem` file. Store it in your
   secret store and delete the download.
6. **Install** the App on the repos you want reviewed. The installation id is the number at the end
   of the installation page's URL (`…/installations/<id>`). It is optional: when it is not set, the
   server looks it up for each repo.
7. Optional: in branch protection, require the `antiphon/approval` status.
8. Optional, for sign-in (section 7.1): in the App's settings, set **Callback URL** to
   `<your Antiphon URL>/api/review-auth/callback`, and under Client secrets, **generate a new client
   secret**. Sign-in needs no extra permission: it reads only the person's login, and it checks their
   repo access with the App's own token (Metadata: read).

**A token instead of an App:** create a fine-grained token with these permissions:
- Pull requests: read and write
- Commit statuses: read and write
- Contents: read
- Metadata: read

Set it as `Review:Secrets:GitHubToken` and leave the `App` keys out. The token can't approve PRs its
owner opened.

**GitHub Enterprise Server:** set `Review:CodeHosts:Hosts:github:BaseUrl` to
`https://<your-host>/api/v3`.

## 4. Configuration

### 4.1 How settings reach the server

Settings use standard ASP.NET Core configuration:
- `server/appsettings*.json`;
- environment variables, where `:` becomes `__` (for example `Review__Polling__Enabled=true`);
- the command line.

In the `Development` environment, the server also reads **user-secrets** with id `antiphon-server`.
The Docker image runs as `Production`, so there it reads environment variables only.

Keep secrets out of any file committed to git. Settings that need a secret don't take the secret
itself: they take the name of the configuration key that holds it (`ApiKeyConfigKey`,
`PrivateKeyConfigKey`, `TokenConfigKey`). The secret can then live in an environment variable,
user-secrets or your platform's secret store.

| Secret | Holds |
|---|---|
| `Review:Secrets:GitHubAppPrivateKey` | The App's PEM key. In a single-line environment variable, write line breaks as a literal `\n`. |
| `Review:Secrets:GitHubToken` | A personal access token, when you don't use an App |
| `Review:Secrets:AnthropicApiKey` | Anthropic API key, used by the `anthropic` completions provider |
| `Review:Secrets:JevApiKey` | Jev key (decisions). It is also the default key for the `litellm-chat` completions provider. |
| `Review:Secrets:GitHubOAuthClientSecret` | The App's client secret, for sign-in (section 7.1) |

### 4.2 A working starting point

This configuration is for a company on github.com, with the Claude Code CLI signed in on the server
machine. The standalone launcher has its own defaults (section 6.1).

```json
{
  "Review": {
    "PublicBaseUrl": "https://review.example.com",
    "DefaultViewerLogin": "your-github-login",
    "CodeHosts": {
      "Hosts": {
        "github": { "Kind": "GitHub", "App": { "AppId": "Iv23xxxxxxxxxxxxxxxx", "InstallationId": 12345678 } }
      }
    },
    "Agents": {
      "Transport": "cli",
      "Repos": [ { "Repo": "acme/widgets", "LocalPath": "/srv/antiphon/mirrors/widgets" } ]
    },
    "Decisions": {
      "Providers": { "haiku-cli": { "Dialect": "claude-cli", "Model": "haiku", "TimeoutSeconds": 120 } },
      "Chains": { "decisions-default": [ { "Provider": "haiku-cli", "Transport": "OneShot" } ] }
    },
    "Completions": {
      "Providers": { "haiku-cli": { "Dialect": "claude-cli", "Model": "haiku", "TimeoutSeconds": 240 } },
      "Chains": { "completions-cheap": [ { "Provider": "haiku-cli", "Transport": "OneShot" } ] }
    },
    "Polling": {
      "Enabled": true,
      "IntervalSeconds": 120,
      "Repos": [ { "Repo": "acme/widgets", "RequireLabel": "antiphon" } ]
    },
    "Policy": { "MergeMethod": "squash" }
  }
}
```

Then set the App's key through an environment variable or user-secrets:
`Review__Secrets__GitHubAppPrivateKey=<PEM>`.

A few notes on this configuration:
- **`Agents:Repos:LocalPath`** is a clone of the repo. For each reviewer, the server fetches the PR's
  head into that clone and checks it out as a detached worktree. Clone it once, for example
  `git clone https://github.com/acme/widgets /srv/antiphon/mirrors/widgets`.
- **`RequireLabel`** limits reviews to PRs that carry that label. Without it, the first poll starts a
  review for every open PR in the repo.
- **The merge gate stays on.** A person confirms each merge. Section 7.4 shows how to switch it off.

### 4.3 Reference

Defaults are shown in parentheses. Lists are indexed (`Repos:0:Repo`) in environment variables and
user-secrets, and are JSON arrays in appsettings.

**`Review`**

| Key | Meaning |
|---|---|
| `PublicBaseUrl` (none) | The UI's address. Commit statuses link to `{PublicBaseUrl}/reviews/{runId}`; without it they carry no link. |
| `DefaultViewerLogin` (`admin`) | The GitHub login that the UI and API act as while sign-in is off (section 7.1) |
| `Viewers:<login>:DisplayName`, `Viewers:<login>:Teams:<n>` | A viewer's display name and teams |
| `AllowViewerHeader` (on in Development only) | Lets the `X-Review-Viewer` request header choose the viewer while sign-in is off. Only for tests, or behind a proxy that sets the header. |
| `InstanceChecksPath` (none) | A folder of checks for every repo (section 9) |
| `DemoSeedEnabled` (on in Development only) | Enables `POST /api/review-runs/demo`, which creates demo runs |

**`Review:Auth`** (sign-in, section 7.1)

| Key | Meaning |
|---|---|
| `Mode` (`None`) | `None`: no sign-in. `GitHub`: sign in with GitHub. Any other value, or `GitHub` without a client id and secret, stops the server at startup. |
| `ClientId` | The App's Client ID (`Iv23…`), or an OAuth App's client id |
| `ClientSecretConfigKey` (`Review:Secrets:GitHubOAuthClientSecret`) | The key that holds the client secret |
| `AllowedLogins:<n>` (none) | Logins that may sign in and decide gates on every repo |
| `RequiredRepoPermission` (`write`) | The least access to a reviewed repo that lets someone sign in and decide its gates: `read`, `write` or `admin`. `none` lets anyone with a GitHub account in. |
| `SessionHours` (168) | How long a session lasts after its last use |
| `KeyRingPath` (none) | A folder for the keys that protect the sign-in cookies. Absent: the server's own data-protection key ring, which in a Linux container makes no keys without a certificate. Set it in Docker; the review compose file does. |
| `WebBaseUrl` (`https://github.com`), `ApiBaseUrl` (derived) | For GitHub Enterprise Server: `https://<host>`; the API root then defaults to `https://<host>/api/v3` |

**`Review:CodeHosts`**

| Key | Meaning |
|---|---|
| `Hosts:<id>:Kind` (`GitHub`) | Host type. The ids `github` and `local` exist by default. |
| `Hosts:<id>:BaseUrl` (`https://api.github.com`) | API root; for Enterprise Server, `https://<host>/api/v3` |
| `Hosts:<id>:App:AppId`, `App:InstallationId`, `App:PrivateKeyConfigKey` (`Review:Secrets:GitHubAppPrivateKey`) | App sign-in, used when both an AppId and a key are present |
| `Hosts:<id>:TokenConfigKey` (`Review:Secrets:GitHubToken`) | Token sign-in, used when there is no App |
| `Hosts:<id>:UpdateMethod` (`rebase`) | How Antiphon brings a PR branch up to date: `rebase` or `merge` |
| `Hosts:<id>:TimeoutSeconds` (30) | Per-request timeout |
| `Repos:<n>:Repo`, `Host`, `LocalPath` | Maps a repo to a host id (default `github`) |

**`Review:Agents`** (the reviewers, section 5.1)

| Key | Meaning |
|---|---|
| `Transport` (`delegation`) | `cli` or `delegation`. Any other value stops the server at startup. |
| `Repos:<n>:Repo`, `LocalPath`, `Project`, `ProjectId` | Where reviewers get a checkout: a clone for `cli`, or an Antiphon project for `delegation` |
| `MirrorRoot` (none) | `cli` only: a folder for the server's own clones. A GitHub repo with no `LocalPath` gets a bare mirror at `<MirrorRoot>/<owner>/<name>.git`, made on its first review. Without it, such a reviewer gets no checkout, only the diff. |
| `Cli:Command` (`claude`), `Cli:Models:deep` (`opus`), `Cli:Models:standard` (`sonnet`), `Cli:MaxParallel` (2) | The `cli` transport |
| `Kind` (ClaudeCode), `Levels`, `Runner`, `ExpectedMinutes` (20) | The `delegation` transport |
| `TimeoutMinutes` (90), `InlinePackMaxChars` (16000) | Both transports |

**`Review:Decisions`** and **`Review:Completions`**: see section 5.

**`Review:Polling`**

| Key | Meaning |
|---|---|
| `Enabled` (false), `IntervalSeconds` (120, minimum 30) | The poll timer. `POST /api/review-polling/poll` runs a pass even when the timer is off. |
| `CommandPrefix` (`/antiphon`) | The prefix for PR comment commands (section 7.5) |
| `Repos:<n>:Repo` | A repo to watch, as `owner/name` |
| `Repos:<n>:BaseBranches` (all), `IncludeDrafts` (false), `SkipLabels` (`antiphon:skip`), `RequireLabel` (none) | Which PRs get a review |

**`Review:Executor`**: `Enabled` (true), `PollSeconds` (10), `LeaseSeconds` (300).

**`Review:Policy`**: see section 8.

## 5. Models: who answers what

The engine makes three kinds of model call:

| Call | What it is | Used by | Settings | Options |
|---|---|---|---|---|
| Reviewers | Agents that can read the repo at the PR's head | The review step, one reviewer per group of checks | `Review:Agents` | `cli` or `delegation` |
| Decisions | Typed questions (yes/no, choice, score) with a probability per answer | Delta, categorise, select checks, base drift | `Review:Decisions` | Jev, `claude-cli` |
| Completions | One structured answer per question | Verifying findings | `Review:Completions` | `anthropic`, `openai-chat` (any OpenAI-compatible endpoint), `claude-cli`, `delegation` |

When nothing can answer, the default workflow carries on rather than stopping:
- **No decision provider:** categorise, select and base drift fail and continue. Every check whose
  triggers match is reviewed, and a base move counts as affecting the PR.
- **No completion provider:** verification is skipped.

So a minimal setup works, but the provider settings below decide how much it costs and how well it
picks checks.

### 5.1 Reviewers

- **`cli`** runs `claude -p` on the server machine, with the tools Read, Grep and Glob only. A group
  with a check of major severity or worse uses `Cli:Models:deep` (`opus`); the rest use
  `Cli:Models:standard` (`sonnet`). At most `Cli:MaxParallel` reviewers run at once. Running reviews
  are held in memory, so a restart loses them; the Refresh button on the run page reruns them.
- **`delegation`** runs each reviewer as a read-only Antiphon agent task on a session runner, with
  Claude Code, Codex or Grok. It needs two things:
  - an Antiphon project whose `GitRepositoryUrl` ends in `owner/name`, or a `Repos` row naming the
    project;
  - a local checkout path for that project.

  This is how the desktop AppHost runs reviews.

**Tokens:** both transports use the agent CLI's own sign-in on the machine that runs it (for
example `claude` logged in to a Claude subscription). There is no API key to set. If the server's
`PATH` doesn't include `claude`, set `Cli:Command` to its full path.

### 5.2 Decisions and completions

Each call goes to a **chain**: a list of providers tried in order. The chain advances when a provider
is unavailable, times out, refuses a question, or answers below its `ConfidenceThreshold`. A
provider rejection stops the chain. Chain entries
take a `Transport`:
- `Http` (the default) for API providers;
- `OneShot` for `claude-cli`;
- `Seat` for delegated providers.

The workflow uses the chains `decisions-default` and `completions-cheap`.

Before a restart, run `dotnet Antiphon.Server.dll --list-review-chains` from the candidate
server's content root with the same environment and configuration arguments as the service.
It prints the effective decision and completion chains, entry indexes, provider names,
transports and dialects, then exits before starting the server. If an existing `OneShot` entry
uses an HTTP provider dialect, the server warns at startup and keeps serving through that HTTP
adapter. Set the entry to `Transport: Http` to match the provider.

**Defaults.** With no decisions configured, the server adds `jev` (with no base URL) and three
placeholder seat providers, and none of these can answer yet. **Configuring any decisions provider
replaces that whole default set**, so list every provider you want. Completions work differently:
built-in providers are always present, and `completions-cheap` is built from whichever are ready
(have their URL and key) unless you configure that chain yourself.

| Provider (completions) | Ready when | Model |
|---|---|---|
| `anthropic` | `Review:Secrets:AnthropicApiKey` is set | `claude-haiku-5-5` |
| `litellm-chat` (`openai-chat`) | You set its `BaseUrl` and `Model`, and its key (`ApiKeyConfigKey`, default `Review:Secrets:JevApiKey`) | Yours |
| `haiku-delegate`, `luna-delegate` | A session runner is available. These are seats, so put them in a chain with `Transport: Seat`. | Claude Haiku, Codex Luna |

So the shortest path to verification without the CLI is to set `Review:Secrets:AnthropicApiKey`.

**`claude-cli`** (decisions or completions) uses a `Model` (`haiku`, `sonnet`, or a full model id)
and the server machine's Claude Code sign-in. It ignores `BaseUrl` and `ApiKeyConfigKey`. For
proxy-backed decisions, use a Jev dialect such as `litellm-systemone` with `Transport: Http`.
Start and sign-in failures stop the chain and appear in the run's node summary. Other CLI errors
and timeouts allow fallback. Give decisions a `TimeoutSeconds` of 120 to cover cold `claude -p`
starts. Keep `ConfidenceThreshold` at 0 for its uncalibrated probabilities.

### 5.3 Jev

Jev ("Jev System One", from TypeSafe) is a typed-decision model. You send it a state and a list of
typed questions:
- `noul`: yes or no;
- `choice`: one of a list of values;
- `score`: one of a set of levels.

It returns each answer with a probability. The engine treats those probabilities as calibrated. That
is why Jev is the first provider in the design's decision chain, with escalation to other models
below a confidence threshold.

| Dialect | Endpoint |
|---|---|
| `typesafe-native` | `{BaseUrl or https://api.typesafe.ai}/v1/systemone` |
| `openrouter-systemone` | `{BaseUrl or https://openrouter.ai/api}/v1/systemone` |
| `litellm-systemone` | `{BaseUrl}/v1/systemone` (a LiteLLM proxy; `BaseUrl` is required) |
| `litellm-passthrough` | `{BaseUrl}/typesafe/v1/systemone` (`BaseUrl` is required) |

```json
"Decisions": {
  "Providers": {
    "jev": { "Dialect": "typesafe-native", "Model": "jev-latest", "ApiKeyConfigKey": "Review:Secrets:JevApiKey", "ConfidenceThreshold": 0.7 },
    "haiku-cli": { "Dialect": "claude-cli", "Model": "haiku", "TimeoutSeconds": 120 }
  },
  "Chains": {
    "decisions-default": [ { "Provider": "jev" }, { "Provider": "haiku-cli", "Transport": "OneShot" } ]
  }
}
```

Set the key as `Review__Secrets__JevApiKey`. `jev-latest` is an alias; pin a version with, for
example, `"Model": "jev-1.14.0"`. The run records the model that actually answered. Cost comes from
the proxy's `x-litellm-response-cost` header when there is one, otherwise from the provider's
`InputPricePerMillionUsd` (default $0.042). Jev's response field names are not final, so the client
reads them leniently.

## 6. Deploying

### 6.1 One machine, standalone (the quickest way to try it)

`scripts/antiphon review-standalone` starts the review-only server and its Vite UI directly.
The standalone UI launcher uses `cmd.exe`. Run the commands from the checkout root.
On Windows, use `scripts\antiphon.cmd` with the same command names and flags.

- Starts Postgres 16 on port 17290 in the `antiphon-review-standalone-pg` container with a dedicated volume.
- Clones the repo to `--mirror-path`.
- Reads the App from Bitwarden and configures the server through environment variables.
- Starts the server on 17262 and the Vite UI on 17263.

```powershell
.\scripts\antiphon.cmd review-standalone --repo acme/widgets --vault-item "GitHub App - acme-review" `
  --viewer-login your-github-login --mirror-path C:\mirrors\widgets --public-base-url https://review.example.com
.\scripts\antiphon.cmd review-standalone --stop
```

- Install Docker, Git, the .NET SDK, Node (`npx`) and the `bw` CLI. Unlock `bw` and sign in to `claude`.
- The Bitwarden item needs `Client ID` and `Installation ID` fields and the PEM key in its notes.
  To deploy without Bitwarden, configure the server directly using section 4.
- Logs go to `logs/review-standalone/`.
- The defaults point at Antiphon's own repo and vault item. Pass `--repo`, `--vault-item`,
  `--viewer-login` and `--mirror-path` for your deployment.
- Polling runs every 120 seconds for `--repo` and considers open PRs without requiring the `antiphon`
  label. Use `--no-polling` to turn it off or `--poll-seconds` to set the interval.
- The command switches off the merge gate for `--repo`, so Antiphon merges approved PRs using the
  server's default `rebase` merge method. Pass `--no-auto-merge` to keep the gate on.
- `--sign-in` enables GitHub sign-in (section 7.1) and allows `--viewer-login` to sign in. It
  requires `--public-base-url` and a `Client secret` field in the vault item.
- Other options include `--no-client`, `--port`, `--client-port` and `--postgres-port`.

### 6.2 Inside an existing Antiphon (AppHost)

`scripts/antiphon review-github-setup` writes the App and review settings into the server's
user-secrets (`antiphon-server` by default).

```powershell
.\scripts\antiphon.cmd review-github-setup --repo acme/widgets --project Widgets `
  --vault-item "GitHub App - acme-review" --viewer-login your-github-login `
  --public-base-url https://antiphon.example.com --dry-run
```

`--dry-run` previews the settings with the key redacted. Run again without that flag to write
the settings, then restart the AppHost. Reviewers use the `delegation` transport on the local
runner, so set the repository URL and local path on the Antiphon project named by `--project`.
The command sets verification to run on a Haiku seat, enables polling and allows authors to
decide their own gates.

Re-run the command after a setting it writes changes. User-secrets persist across restarts and new
builds.

### 6.3 Docker

`docker-compose.review.yml` runs the review engine and nothing else. It has three services:
- **`antiphon`:** the server and its built UI on port 8080, with a health check at `/health`. It runs
  as user 1654 in the `Production` environment, so user-secrets, demo runs and the viewer header are
  off. It has no session runner, Hangfire or message broker. The image is built with the Claude Code
  CLI.
- **`postgres`:** `postgres:16`, with its own volume.
- **`state-init`:** runs once before the server and makes the state volume writable for user 1654.

No image is published; compose builds it from the repo.

**Set it up:**
1. Copy the template and fill it in. `review.env` is ignored by git and left out of the Docker build.
   ```bash
   cp docker/review.env.example review.env
   ```
   It holds the App (id, installation id, and the key on one line with `\n` for each line break), the
   repos to watch, the Claude sign-in, the UI's address, the merge method and who is acting. Any other
   `Review__` setting from section 4.3 can go there too.
2. Start the stack:
   ```bash
   docker compose -f docker-compose.review.yml --env-file review.env up -d --build
   ```
3. Open `http://127.0.0.1:8080`. `REVIEW_BIND_ADDRESS` and `REVIEW_BIND_PORT` in `review.env` change
   where it listens. To let other people in, put a reverse proxy with TLS in front (section 6.4).

**Claude sign-in.** Reviewers, decisions and verification all run `claude -p` inside the server
container. Set one of these in `review.env`:
- `CLAUDE_CODE_OAUTH_TOKEN`, for a Claude subscription. Run `claude setup-token` on any machine and
  paste the token it prints.
- `ANTHROPIC_API_KEY`, for an API key billed per token.

The image holds no credential. Deep groups use `opus`, standard groups `sonnet`, and decisions and
verification `haiku`. `REVIEW_DEEP_MODEL`, `REVIEW_STANDARD_MODEL` and `REVIEW_CHEAP_MODEL` change
them, and `REVIEW_MAX_PARALLEL` (2) caps the reviewers that run at once.

**Clones.** You don't clone anything. `Review:Agents:MirrorRoot` is `/state/mirrors`. On a repo's first
review, the server creates a bare mirror there (`/state/mirrors/<owner>/<name>.git`). It fetches each
PR's head into the mirror with the App's token. The token reaches git only through the git process's
environment: it is not in its arguments and is never written to the mirror.

**Where things are kept.** The `review-state` volume holds the mirrors, the logs (`/state/logs`) and
the keys that protect sign-in cookies (`/state/review-signin-keys`). The `review-pgdata` volume holds the runs.
`docker compose -f docker-compose.review.yml down` keeps both volumes; adding `-v` deletes them.

**Which settings win.** `docker-compose.review.yml` sets the fixed review-only settings: the
transport, the mirror root, the model providers and the switches that turn the rest of Antiphon off.
For those keys it wins over `review.env`.

**The image on its own.** `docker build -t antiphon-server .` builds the server without the CLI; add
`--build-arg INSTALL_CLAUDE_CODE=true` to include it. `docker-compose.yml` is the full Antiphon stack
with a session runner, and it has no review settings.

**One startup error is expected.** The log shows `The key ring does not contain a valid default
protection key` once at startup. That is the server's own key ring, which protects stored agent API
keys; a review-only server stores none, so it needs no certificate. Sign-in uses its own key ring
(`/state/review-signin-keys`).

**What has been tested.** On 2026-11-24 this compose file was built and started on a Linux host with
placeholder settings. These checks passed:
- `/health` and `/api/version` answer, the UI loads, and the review API answers;
- the server runs as user 1654 with a writable home, `claude --version` reports the pinned version,
  and `claude -p` without a sign-in says it is not logged in rather than failing;
- with sign-in on, the API answers 401 without a session, login redirects to GitHub, a forged
  callback is refused, and the sign-in key is written to the state volume;
- with sign-in off, the API answers as the default viewer.

Tests also cover the mirror against a local git origin.

**End to end, also on 2026-11-24.** The same compose file ran against a real GitHub App and a Claude
subscription token (`CLAUDE_CODE_OAUTH_TOKEN`), behind HTTPS, polling a small private demo repo with
three open PRs. In about two minutes all three ran from intake to their next wait:
- **A docs-only PR:** categorised `docs`, needed no reviewers, was approved by the App's bot with
  both `antiphon/*` statuses green, and waited at the merge gate.
- **A feature PR:** three Opus reviewer groups ran, verification passed, and the bot posted a review
  with an inline finding. The `antiphon/review` status linked to the run page.
- **A PR with two planted defects:** both were found as major findings. A status change that skipped
  the board's rules was caught by one of the repo's own checks (section 9). A date comparison with
  its operands swapped was caught by the built-in correctness check.

The server created the mirror itself, and a full GitHub sign-in in a browser worked: the UI was sent
to GitHub, came back signed in, and showed the login with a Sign out item.

### 6.4 Exposing the UI

- **Sign-in off (the default):** anyone who can reach the UI acts as `DefaultViewerLogin`. Put it
  behind a private network: a VPN, a tailnet, or a reverse proxy with its own authentication.
- **Sign-in on (section 7.1):** the API needs a GitHub session, so the UI can be reached from the
  internet. Serve it over HTTPS. The session cookie is marked Secure when `PublicBaseUrl` starts with
  `https://`. The UI's own files stay public, but they hold no data.

Either way, set `Review:PublicBaseUrl` to the address people use. The links in GitHub statuses use it,
and so does the sign-in callback.

Serve it over HTTPS even on a private network. Browsers set to always use secure connections refuse
a plain `http://` address with an error page; Chrome did so for a tailnet address in testing. If you
change from `http://` to `https://`, update the App's callback URL too (section 3), and redirect the
old address so the links already posted on PRs keep working.

## 7. People, gates and merging

### 7.1 Signing in, and who is acting

**With sign-in off (the default),** every UI and API request acts as `Review:DefaultViewerLogin`, or
as `admin` when that is not set. With `AllowViewerHeader` on, the `X-Review-Viewer` header can name a
different viewer. The UI does not send that header, so only a proxy or a test would.

**To turn on sign-in with GitHub:**
1. In the GitHub App's settings, set the callback URL and generate a client secret (section 3, step 8).
2. Set these, the secret through your secret store:
   ```
   Review__Auth__Mode=GitHub
   Review__Auth__ClientId=Iv23xxxxxxxxxxxxxxxx
   Review__Secrets__GitHubOAuthClientSecret=<the client secret>
   Review__PublicBaseUrl=https://review.example.com
   ```
3. Restart the server. Its log says `Review sign-in: GitHub (client …)`, and
   `GET /api/review-auth/status` answers `"mode": "GitHub"`.

What happens then:
- **Every `/api` and `/hubs` request needs a session**, apart from `/api/review-auth/*` and
  `/api/version`. Without one, the API answers 401 `sign_in_required`. The UI then sends the
  browser to GitHub, and back to the same page afterwards. `/health` stays open.
- **Who may sign in:**
  - a login in `Review:Auth:AllowedLogins`;
  - anyone with at least `RequiredRepoPermission` (default `write`) on one of the repos this
    instance reviews. These are the repos in `Review:Polling:Repos`, `Review:Agents:Repos` and
    `Review:CodeHosts:Repos`, plus every GitHub repo that has a review run.

  Antiphon asks GitHub with the App's own token (`GET /repos/{repo}/collaborators/{login}/permission`).
  Anyone else sees a "No access" page and gets no session.
- **Who acts:** the signed-in login. The header shows it, with a **Sign out** item. The
  `X-Review-Viewer` header is ignored.
- **Sessions:** a cookie, `antiphon.review.session` (HttpOnly, SameSite=Lax). It lasts `SessionHours`
  (a week by default) after its last use. Repo access is read once, at sign-in, so after someone's
  access changes they sign out and in again. The cookie's keys are in `Review:Auth:KeyRingPath` when
  it is set, else in the server's own key ring. Keep that folder across restarts, or everyone signs in
  again. In a Linux container, set it (the review compose file uses `/state/review-signin-keys`):
  there, the server's own key ring makes no keys without a certificate, and sign-in fails.
- **Turn it on only on a server that runs the review engine alone,** such as `review-standalone`
  or the review compose file. On a full Antiphon, the session runner and the scripts call the API
  without a session, so they would be locked out.

In both modes:
- **PR comment commands** act as the GitHub user who wrote the comment.
- **The poller** acts as `antiphon`, and everything is posted on GitHub as the App's bot.

### 7.2 Who may decide a gate

Anyone may decide a gate, except the PR's author, compared case-insensitively. A repo listed in
`Review:Policy:AuthorMayDecideRepos` lets the author decide too. With sign-in on, a person also needs
`RequiredRepoPermission` on the PR's repo, while a login in `AllowedLogins` may decide on every repo.
There are no team checks yet.

With sign-in off, the UI acts as one login. So set `DefaultViewerLogin` to your own login, and add
your repo to `AuthorMayDecideRepos` if you review your own PRs. Without that, you can't decide gates
on your own PRs.

### 7.3 The gates

| Gate | Opens when | Decisions | Can be switched off |
|---|---|---|---|
| `human-approve` | A clean round needs a person. The reasons include a human-required category, a large or risky change, an unverified high-severity finding, or conceded disputes (section 8). Once one round of a PR needs a person, every later round of that PR does too. | Approve, another round (with an optional steer), close | No |
| `human-review` | The bot-only round limit is reached (`HumanGateAfterBotOnlyRounds`, default 3), or a loop goes past its iteration limit | Approve, another round, close | No |
| `merge-gate` | After every approval, from the bot or a person | Merge, don't merge | Yes (`optional: true`) |

What each decision does:
- **Approve** on `human-approve` or `human-review` sets `antiphon/approval` to success at the reviewed
  head.
- **Another round** reviews again, and passes any steer text to every reviewer.
- **Close** stops reviewing the PR.

The merge gate passes by itself when there is nothing for Antiphon to merge: no PR, or a code host
that cannot merge. A person can also merge on GitHub at any time, and that ends the run.

### 7.4 Switching a gate off, and merging

`Review:Policy:GatesOff:<node-id>` lists the repos (`owner/name`, or `*` for every repo) where a gate
passes without a person. Only gates the workflow marks `optional: true` can be switched off, and
today that is only `merge-gate`.

```
Review__Policy__GatesOff__merge-gate__0=acme/widgets
```

Once the merge gate passes, Antiphon does the following:
1. It checks whether the base branch moved, and updates the branch if it did.
2. It merges with `Review:Policy:MergeMethod` (`rebase` by default, or `squash` or `merge`). The merge
   is pinned to the approved head, so GitHub refuses it if someone pushed in the meantime.
3. If GitHub refuses, the step is marked failed and the PR waits for a person to merge.

The run closes when GitHub reports the PR merged.

### 7.5 PR comment commands

Comments are acted on only when the author is an owner, member or collaborator of the repo:
- `/antiphon review [steer]` starts a review, or asks for another round, with an optional steer
  for the reviewers.
- `/antiphon recheck` does the same with no steer.

A command must begin the comment. Antiphon reacts with 👀 and replies with what it queued. The
runs page also has **Check GitHub now**, which runs a poll pass straight away.

## 8. Policy

These are the `Review:Policy` keys and their defaults. They apply to every repo.

| Key | Default | Meaning |
|---|---|---|
| `HumanGateAfterBotOnlyRounds` | 3 | Bot-only rounds before `human-review` opens |
| `Post:<n>` (`Round`, `MinSeverity`, `MinConfidence`, `MaxNits`, `NitsInline`) | Round 1: nit and above at 0.5, at most 5 nits, not inline. Round 2: minor at 0.65. Round 3: major at 0.8. | What each round posts |
| `BypassCategories` | security, data-loss | Checks in these categories get their own reviewer and are posted every round |
| `HumanRequiredCategories` | migration, auth, security, public-api, infra, session-runtime | A change in one of these needs a person to approve |
| `HumanRequiredExternalContractAtLeast`, `HumanRequiredReversalAtLeast` | 4, 4 | Blast-radius scores (1 to 5) that need a person |
| `LargeSizes` | L, XL | Change sizes that need a person |
| `HumanRequiredConcededDisputesAtLeast` | 2 | Conceded disputes that need a person |
| `HumanRequiredWhenUnverifiedHighSeverity` | true | A selected major or blocker check whose findings could not be verified needs a person |
| `SelectThreshold`, `VerifyDropBelow` | 0.5, 0.5 | The probability needed to select a check, and the probability below which verification drops a finding |
| `GroupTokenBudget`, `PackDiffTokenBudget` | 6000, 24000 | The size of each reviewer's group of checks, and of the diff in its context |
| `BaseDriftAffectedThreshold` | 0.35 | The probability above which a base move counts as affecting the PR |
| `KeepUpToDate` | true | Keep approved branches up to date with the base |
| `ContractGlobs` | proto, openapi, graphql, Migrations, sql files | Paths that count as an external contract |
| `OnFailure:<node-id>` | none | `continue` or `stop` for that step, overriding the workflow |
| `StatusesRequired` | false | When true, a commit status GitHub refuses fails the step |
| `AuthorMayDecideRepos:<n>` | none | Section 7.2 |
| `GatesOff:<node-id>:<n>` | none | Section 7.4 |
| `MergeMethod` | rebase | Section 7.4 |

## 9. Tuning the review: checks and prompts

A **check** is one thing reviewers look for, written as a Markdown file with YAML front matter. The
server reads checks from three places:
1. **The instance:** a folder named by `Review:InstanceChecksPath`, laid out as
   `<category>/<id>.md`. These apply to every repo.
2. **The repo:** `.antiphon/review/checks/**/*.md` in the repo itself, read at the PR's base commit.
   A repo check with the same `id` replaces the instance's.
3. **Built-in seeds:** seven checks compiled into the server, one each for correctness, security,
   data, concurrency, errors, tests and contracts. **They are used only when there are no instance
   checks and no repo checks.** Your first check of your own turns all seven off, so copy any you
   want to keep from `server/Application/Review/Checks/Seed/`.

```markdown
---
id: ACME-0001
title: Untrusted input reaches a query, command, path or secret
severity: blocker            # nit | minor | major | blocker
status: active               # candidate | shadow | active | retired; only active checks run
requiresHuman: false         # true: a PR this check applies to needs a person to approve
flags: [security]            # a flag in BypassCategories: own reviewer, posted every round
triggers: { tags: [external-contract, auth, security], globs: ["**/*.cs", "**/*.ts"] }
---
# @model: Selector
Does this change handle input from users, requests or files, or touch credentials?
# @model: Prompt
Look for untrusted data that reaches an interpreter without validation or parameterisation ...
# @model: Examples
Bad: `cmd.CommandText = "SELECT * FROM t WHERE id = " + id;`
# @humans: Why this check exists
Injection and leaked secrets are the most common severe defects in reviewed code.
```

How the fields are used:
- **`triggers`** decides which changes a check is a candidate for:
  - `globs` match file paths;
  - `languages` match file languages;
  - `tags` match the categories the categorise step gives the change;
  - `symbols` match text in the diff.

  A check with no triggers is a candidate for every change.
- **`@model: Selector`** is the yes/no question that decides whether the check applies.
- **The other `@model` sections** go to the reviewer.
- **The first `@humans` section** is shown next to the check's findings.
- **`category`** comes from the check's folder unless the front matter sets one.

**Prompts.** The prompts around the checks are part of the server's code, not files you can edit
today:

| Prompt | File |
|---|---|
| The reviewer's brief | `server/Application/Review/DelegationReviewAgentRunner.cs`. The `cli` transport uses the same brief. |
| The reviewer's answer format | `server/Application/Review/Abf/AbfTemplates.cs` (see [the Block Format spec](../../specs/block-format/v1.md)) |
| Verification | `server/Application/Review/Execution/ReviewReviewSteps.cs` |
| Categorise questions | `server/Application/Review/ReviewCategorisation.cs` |
| Base-drift question | `server/Application/Review/Execution/ReviewBranchSteps.cs` |
| The decision prompt for `claude-cli` | `server/Infrastructure/Review/ClaudeCliModels.cs` |

So you tune a review through checks: what to look for (`@model` sections) and when it applies
(`triggers` and the selector). A repo `prompts/` folder, `groups/` and `policy.yaml` are in the design
(section 3.6) but are **not built yet**.

## 10. Changing the workflow

The workflow is `server/Application/Review/Workflows/pr-review.yaml`. It is compiled into the server,
so changing it means editing that file and rebuilding. Each run keeps the version of the workflow it
started with (stored by hash), so a change applies to new runs only. The run page's Workflow tab
shows a run's graph.

**Nodes** have a `kind`:
- `code`, `decision` and `completion` run a step named by `run`;
- `gate` waits for a person;
- `wait` parks the run until a code-host event (`on: [push, merge, close, base-moved]`, plus
  `recheck`);
- `fan-out` and `join` run the reviewers and gather their results.

Params that work today:

| Param | Kinds | Effect |
|---|---|---|
| `run` | code, decision, completion | The step to run (table below) |
| `outputs` | code, decision | The outcomes the step can take. The validator checks that edge labels match them. |
| `onFailure` (`stop`), `fallback` | work nodes | `continue` goes on, taking `fallback` as the step's outcome if one is set |
| `chain` | decision, completion | The model chain to use |
| `decisions` | gate | Any of `approve`, `another-round`, `close` |
| `gateKind` | gate | `ReviewGate`, `ApprovalGate` or `MergeGate` |
| `optional` | gate | Lets `Review:Policy:GatesOff` switch the gate off |
| `on` | wait | The events that end the wait |
| `node`, `from` | fan-out, join | The reviewer node, and the fan-out to gather |

The YAML also accepts these params, but nothing acts on them yet:
- `queue`, `autoRetries` and `autoDecision` on gates;
- `timeout` on waits;
- `over` and `policy` on fan-outs;
- `role`, `output` and `runner` on agent nodes.

**Edges:**
- `a -> b -> c` is a chain.
- `a[x|y] -> b` follows the edge when `a`'s outcome is `x` or `y`.
- The mapping form `{ edge: "a[x] -> b", maxIterations: 20, overflow: human-review, description: … }`
  limits a loop: past the limit, the run goes to the overflow gate.
- An edge into `intake` starts a new iteration.
- Every cycle needs a gate, a wait or `maxIterations`.

**Steps available to `run`:** `Intake`, `Delta`, `Categorise`, `SelectChecks`, `GroupChecks`,
`BuildPacks`, `CollateFindings`, `VerifyFindings`, `Publish`, `RouteAfterRound`,
`NeedsHumanApproval`, `BotApprove`, `RecordBaseDrift`, `CheckBaseMoved`, `BaseDriftCheck`,
`UpdateBranch`, `Merge`. Section 4 of design.md describes what each one does.

**Adding a gate** with the existing decisions and kinds needs YAML only. For example, this adds a
release sign-off after the merge gate that can be switched off per repo:

```yaml
  release-signoff: { kind: gate, gateKind: ApprovalGate, optional: true, decisions: [approve, close],
                     description: "A release manager signs off before the merge." }
edges:
  - merge-gate[approve] -> release-signoff        # replaces merge-gate[approve] -> base-moved
  - release-signoff[approve] -> base-moved
```

A new decision name, such as "approve with conditions", or a new gate kind needs code changes:
- the `ReviewGateDecision` or `ReviewGateKind` enum in `server/Domain/Enums/ReviewEngineEnums.cs`;
- the decision mapping in `ReviewService.cs`;
- the UI text in `client/src/features/review/run/gateRules.ts` and `reviewMeta.ts`.

**Adding a step** needs code:
1. Write a method `Task<NodeResult> MyStepAsync(ReviewWalk walk, WorkflowNode node, ReviewNodeExecution
   execution, CancellationToken ct)`. Return `NodeResult.Ok(outcome, summary, output)`: the outcome
   picks the edge. Model calls go through `DecisionChain` or `CompletionChain`, which you get from
   `walk.Services`.
2. Register it in `Steps` in `server/Application/Review/ReviewWorkflowExecutor.cs`. Optionally map it
   to a stage in `StageOf` there.
3. Add the node to the YAML with `run` and `outputs`, plus its edges.
4. Extend `tests/Antiphon.Tests/Review/ReviewWorkflowValidatorTests.cs` and
   `ReviewEndToEndApiTests.cs`. The validator does not check that a `run` name exists: an unknown
   name only fails when a run reaches it.

`POST /api/review-workflows/validate` validates a graph, given as the JSON the run page's Workflow tab
uses.

## 11. Check that it works

1. `GET /health` answers.
2. `GET /api/review-polling` lists your repos, and after the first pass `lastPass` shows `ok: true`.
3. Open a test PR, with the `RequireLabel` label if you set one. Then click **Check GitHub now** on
   the runs page, or comment `/antiphon review` on the PR.
4. On the run page, watch the steps go through intake, review, publish, and then the decision. On
   GitHub, the PR gets a review from the App's bot and the two `antiphon/*` statuses.
5. For an automated live check, run the command below with your scratch PR number. Label the PR
   `antiphon:live-check` and `antiphon:skip`. The test keeps the merge gate on.

   ```sh
   scripts/antiphon review-live-github --pr 123 --repo acme/widgets --vault-item "GitHub App - acme-review"
   ```

In `Development`, `POST /api/review-runs/demo` fills the UI with demo runs, so you can look around
before connecting GitHub.

## 12. Not built yet

These are in [design.md](design.md) but not in the code:
- Webhooks; polling is the only trigger.
- Sign-in with anything but GitHub, and checking who may decide a gate by team or trust. Sign-in
  checks repo access only.
- Team sync from CODEOWNERS or GitHub teams; gate queues by team.
- Repo-level `policy.yaml`, prompts, groups and workflow (`.antiphon/review/`); only `checks/` is read.
- Saving workflow edits from the UI.
- Learn proposals and escalated disputes as gates.
- Seat decision providers.
- AI conflict resolution when updating a branch.
- Check runs.
- Dismissing the bot's GitHub approval when a new round starts.
