# External tools: vetting record

Every external skill, agent or MCP server goes through the vetting gate in this order:

1. Read it fully.
2. Check its provenance.
3. Test it in a scratch project.
4. Compare results with and without it.
5. Decide.

Items marked **pending** have not passed step 3 or 4 and are not used by the harness.

Checked on 2026-11-07 with Claude Code 2.1.319, .NET SDK 10.0.403 and 8.0.411, Node 24.13.1, Windows PowerShell 5.1 and pwsh 7.6.9.

## dotnet/skills (MIT, .NET team at Microsoft)

Distributed as a Claude Code plugin marketplace with `/plugin marketplace add dotnet/skills`.

### `dotnet-test` plugin 1.0.1

The plugin has 26 skills and 10 agents.

**Step 1, read:**
- No risky code was found.
- A pattern scan flagged only two `extensions/powershell.md` files, which are Pester guidance that names `Invoke-WebRequest` as something tests should avoid.
- The plugin includes PowerShell helpers and one Python script (`find_untested_sources.py`).
- Its agents are `.agent.md` files written for GitHub Copilot. Whether they load in Claude Code has not been checked.

**Most relevant skills:**
- **`run-tests`** maps each mode (classic, VSTest, VSTest-to-MTP bridge with `--`, native MTP with `--project`) to:
  - the command shape
  - the filter syntax for each framework (xUnit v3 on MTP uses `--filter-class` / `--filter-trait` / `--filter-query`)
  - the TRX and diagnostic flags
- **`platform-detection`** works out how MSBuild properties take precedence when choosing between VSTest and MTP.
- **`filter-syntax`**, **`assertion-quality`** and **`test-anti-patterns`** are possible inputs for `framework-review`.

**Conflict risk:**
- The `run-tests` description says "ALWAYS USE before running .NET tests", which competes with our `run-tests` skill and `suite-runner` agent.
- `run-tests` reads a repository overlay at `.agents/skill-overlays/dotnet-test/run-tests.md`. The overlay needs frontmatter with `core: dotnet-test/run-tests`, `binding-revision: "1"` and `mode: extend`.
- This gives a clean way to integrate if the plugin is installed: a harness-generated project could ship an overlay that contains its exact commands.

**Decision: take the idea only; the plugin is an optional install.**
- The Phase 1 spikes confirmed its MTP command and filter facts (see `framework-matrix.md`): `--project`, `--report-trx`, `--filter "TestCategory=x"` for NUnit and MSTest, `--filter-trait` for xUnit v3. These go into the `test-framework` and `run-tests` playbooks, written from our spike results.
- The plugin is not installed by `setup.ps1`. Its `run-tests` skill would compete with ours, and its agents are written for GitHub Copilot.
- If a user installs it anyway, `scaffold` writes `.agents/skill-overlays/dotnet-test/run-tests.md` with the project's exact commands, so both skills give the same commands.
- Whether the plugin loads that overlay in Claude Code is checked in the Phase 7 acceptance pilot.

### `eng/skill-validator`

- **`check`** does static analysis of skills, plugins and agents, with no LLM involved.
- **`evaluate`** runs an A/B comparison with an LLM judge, but needs a GitHub Copilot token through the Copilot SDK, so it doesn't fit a Claude-only setup.
- Binaries are published only as `skill-validator-nightly` builds, which are mutable and can't be pinned properly.
- The source repo pins an SDK 11 preview, but the validator itself targets `net10.0`. It **built cleanly with SDK 10.0.403** in a scratch copy.
- **Test run** with `check --skills` on the 24 pom-harness skills:
  - By default it flags every `../` link as an error. `--allow-repo-traversal` turns this off, and we need that because our skills deliberately link to each other.
  - It found one real error: an XML-like tag (`<repository>`) in a skill description, which Claude rejects.
  - It warned about missing code blocks or numbered steps (19 and 7 skills).
  - It warned that `test-case-design` is close to the size where gains drop off (3.7k tokens).

**Decision: adopt as an optional development check for this repo only.**
- Build it from source at a pinned dotnet/skills commit and run it with `check --allow-repo-traversal --skills .claude/skills`.
- Do not depend on the nightly binaries.
- `Harness.Tools validate-skills` is still required, because it covers the harness-specific checks: the routing table, leftover TypeScript or Azure DevOps terms, and links.
- Add the XML-tag rule and a token-size warning to `validate-skills`, so consumers get them without this tool.

## Live page observation: Playwright MCP vs playwright-cli

| | `@playwright/mcp` | `@playwright/cli` |
|---|---|---|
| Version (npm, 2026-11-07) | 0.0.84 (modified 2026-11-03) | 0.1.23 (modified 2026-10-31) |
| License | Apache-2.0 | Apache-2.0 |
| Owner | Microsoft (official) | Microsoft (official) |

**Flags confirmed in the MCP README:**
- `--allowed-origins` and `--blocked-origins`, both semicolon-separated. The blocklist is checked first. The README says this is **not a security boundary** and does not affect redirects.
- `--isolated`, which keeps the browser profile in memory only.
- `--output-dir`
- `--snapshot-mode full|none`
- `--secrets <dotenv file>`, which redacts matching text in tool responses. The README calls it "a convenience and not a security feature".
- `--redact-url-params`

**Effect on harness docs:**
- `docs/mcp.md` must not describe origin limits or secret redaction as protection.
- Throwaway test accounts remain the real safeguard.

The README says the CLI with skills uses fewer tokens than the MCP server, but it gives no numbers.

Both packages depend on `playwright` **1.65.0-alpha-2026-10-30**. Neither has install scripts, and they were installed with `--ignore-scripts`. Both ran against the installed Chrome (`--browser chrome`).

**Measured on 2026-11-07.**

- **Task:** on `https://demo.playwright.dev/todomvc/`, open the page, snapshot, add "buy milk", snapshot, complete it, snapshot, and check that "0 items left" appears.
- **MCP options:** `--headless --isolated --allowed-origins https://demo.playwright.dev`, driven over stdio JSON-RPC by a C# file-based app.
- **CLI:** driven from PowerShell.

| | Playwright MCP 0.0.84 | playwright-cli 0.1.23 |
|---|---|---|
| Task completed | yes | yes |
| Characters returned for the task | 5,151 | 5,615 |
| Fixed context cost | **25 tool schemas, 21,271 characters, loaded in every session** | Bundled `SKILL.md` of 15,702 characters, loaded only when the skill is used |
| Seen by our hooks | **No.** MCP calls bypass the `Bash` and `PowerShell` matchers | **Yes.** It runs through the shell, so `pre-shell` can guard it |
| Origin limits | `--allowed-origins` flag | Same `network.allowedOrigins` key in the config file (`--config`, or `.playwright/cli.config.json` by default). Tested live, see below |
| Code snippets in output | none | Each action prints the JavaScript it ran (`### Ran Playwright code`). Skills must ignore it and write C# instead |

**Decision: adopt `playwright-cli` 0.1.23, pinned, as the default page-observation tool.**
- Playwright MCP stays as a documented alternative that is off by default.
- Reasons for the CLI:
  - it has no fixed context cost
  - our hooks can see it
  - it completes the task as well as the MCP
- Our `automate-test` and `execute-test` playbooks will contain a short C#-oriented command guide instead of the bundled `SKILL.md`. That avoids loading 15.7 KB, and avoids the bundled skill's guidance on generating TypeScript tests.

**Origin limit, tested live on 2026-11-07.**
- Setup: `.playwright/cli.config.json` set to `{"network":{"allowedOrigins":["https://demo.playwright.dev"]}, "browser":{"isolated":true}}`. The CLI picked up the file without a `--config` flag.

| Action | Result |
|---|---|
| `open https://demo.playwright.dev/todomvc/` | Loaded |
| `goto https://example.com/` | `net::ERR_BLOCKED_BY_CLIENT` |
| `fetch('https://example.com/')` from the allowed page | `Failed to fetch` |
| `fetch` to the same origin from the allowed page | OK |

- The harness ships this file, with the test environment's origin, as the automate-test asset `.claude/skills/automate-test/assets/cli.config.json`, which the skill copies to `.playwright/cli.config.json` with the app's origin.
- As the MCP README says for the same feature, this is **not a security boundary**: redirects are not covered. Throwaway test accounts remain the real safeguard.
- The CLI writes snapshots and console logs to `.playwright-cli/` in the working directory. The consumer `.gitignore` must exclude that folder.

**Commands the skills use, verified on 2026-11-08** with 0.1.23 against `https://demo.playwright.dev/todomvc/`, with the origin-limit config above, on pwsh 7.6.9 and Windows PowerShell 5.1:

| Command | Result |
|---|---|
| `npx --yes @playwright/cli@0.1.23 --version` | `0.1.23`. This is also the availability check |
| `… open <url> --browser chrome` | Opened the installed Chrome. Prints the page URL, title, and a link to the snapshot file |
| `… snapshot` | An accessibility tree in YAML. Each element has its role, its accessible name and a ref such as `[ref=e8]` |
| `… fill e8 "buy milk"` | Filled the textbox. Printed the JavaScript it ran: `page.getByRole('textbox', { name: 'What needs to be done?' }).fill(…)` |
| `… press Enter` | Pressed the key and printed a new snapshot link |
| `… check e21`, `… click e30` | Checked the checkbox, followed the link. Each printed the `getByRole(…)` it used |
| `… generate-locator e8` | `getByRole('textbox', { name: 'What needs to be done?' })`. That is JavaScript syntax; in C# it becomes `GetByRole(AriaRole.Textbox, new() { Name = "What needs to be done?" })` |
| `… find "items left"` | The matching snapshot nodes with their context, or `No matches found` |
| `… requests` | The network requests since the page loaded (static files hidden unless `--static`) |
| `… console` | The console messages with counts of errors and warnings |
| `… goto https://example.com/` | `net::ERR_BLOCKED_BY_CLIENT` (the origin limit) |
| `… close` | `Browser 'default' closed` |

- Refs of elements still on the page were unchanged after an in-page update (`e8` stayed `e8`), and new elements got new refs. Skills still take each ref from the latest snapshot.
- Outputs are short: 50 to 1,700 characters per command on this page. A snapshot of a large page is longer. `find` returns only the matching part.
- **PowerShell detail:** calling `npx` through a PowerShell function that forwards `$args` lost the arguments (the CLI printed its help). Calling `npx --yes @playwright/cli@0.1.23 <command>` directly works on both editions.
- **Not verified:** `select`, `upload`, `dialog-accept`, the `request <n>` detail commands, and pages that make API calls. The demo page has none.

## Documentation servers

**Tested on 2026-11-07** with three questions whose answers the Phase 1 spikes verified:

1. How to override `ContextOptions()` in a Playwright .NET `PageTest`.
2. How to filter MSTest by `TestCategory` under MTP.
3. How to filter xUnit v3 by trait under MTP.

Both servers were driven over streamable HTTP by a small PowerShell client, without an API key.

| | Microsoft Learn MCP (`https://learn.microsoft.com/api/mcp`) | Context7 (`https://mcp.context7.com/mcp`) |
|---|---|---|
| Tools, schema size | 3 tools, 4,928 characters | 2 tools, 5,262 characters |
| 1. Playwright `ContextOptions` | **Wrong.** Only Azure Playwright JavaScript pages | **Right.** `public override BrowserNewContextOptions ContextOptions()` from `/websites/playwright_dev_dotnet` |
| 2. MSTest filter | **Right.** `--filter "…TestCategory=CategoryA"` | **Right.** `dotnet test --project … --filter "TestCategory=…" --report-trx` from `/microsoft/testfx` |
| 3. xUnit v3 trait filter | **Right.** Lists `--filter-trait` and the other xUnit options | **Wrong** on both `/xunit/xunit` and `/websites/xunit_net`. It returned the in-process `XunitFilters` API instead |
| Characters per answer | about 21,000 to 23,000 | about 4,300 to 5,500 |

| Candidate | Decision |
|---|---|
| Microsoft Learn MCP | **Adopt as optional, off by default.** Best for the .NET test platform and SDK. Its answers are large, so the skill tells the agent to set a token budget where the client supports one. Queries go to a Microsoft-hosted service |
| Context7 | **Adopt as optional, off by default.** Best for the Playwright .NET API. Sends queries to a third-party hosted service, and works without a key at a lower rate limit. The Claude plugin in this environment asked for authentication, so the documented setup is the plain HTTP server entry |
| NuGet MCP (`NuGet.Mcp.Server` 1.4.23, Microsoft, verified owner) | **Reject.** It starts through `dotnet dnx NuGet.Mcp.Server@1.4.23 --yes` and offers 6 tools, but their schemas are **35,406 characters**, loaded in every session. On a spike project, `upgrade_packages_to_latest` returned `"NuGetSolver could not find a compatible set of package versions … for an unknown reason"` |

Neither documentation server is required, and every skill must work without them. The skill that uses them says which server to try for which question, and that their answers are checked against `framework-matrix.md` before use.

**Open item for `csharp-standards` (Phase 3):** on the same spike project, `dotnet list package --outdated` and `dotnet package list --outdated --format json` also failed. Every package was reported as "Not found at the sources", although all are current on nuget.org and no `nuget.config` was involved. `--vulnerable` worked. The cause must be found before a skill relies on the outdated check.

## GitHub CLI (`gh`) for CI tasks

- Official GitHub tool, MIT license. Version 2.103.3 (2026-10-26) is installed.
- Logged in through the keyring with scopes `repo`, `workflow`, `read:org` and `gist`.
- Replaces the GitHub MCP server, which is rejected: it adds context cost, our hooks can't see it, and it failed to connect in this environment.

**Tested read-only** against the sample project's failed nightly run:

| Command | Result |
|---|---|
| `gh run list --json databaseId,workflowName,conclusion,headBranch` | OK |
| `gh run view <id> --json jobs`, then `ConvertFrom-Json` | OK. Jobs with their conclusions |
| `gh api repos/<owner>/<repo>/actions/runs/<id>/artifacts`, then `ConvertFrom-Json` | OK. 15 artifacts |
| `gh run download <id> -n results-local-api -D <scratch>` | OK. `results-slim.json` with per-test status, 7.8 KB |
| `gh run view <id> --log-failed` | Works, but returned **1.5 MB**. It must never go straight into the conversation |
| `--jq '<filter with "…">'` on Windows PowerShell 5.1 | **Breaks.** 5.1 splits the argument and gh fails with "accepts at most 1 arg(s), received 2" |

**Decision: adopt as the required tool for CI and pull-request skills.** It is optional elsewhere: the core harness works without it.

Used by these skills and agents:
- `ci-pipeline`: `gh workflow run` and `gh run watch`, but only when the user asks.
- `maintain-test`: `gh run list --json …`, `gh run view <id> --json jobs` and `gh run download <id> -n <artifact> -D TestResults/ci/<id>`, to read the TRX from CI. Never `--log-failed` into the conversation. When a log is really needed, `suite-runner` extracts only the failing step.
- `pr-review-loop`: `gh pr view`, `gh pr checks` and `gh api` for review comments.
- `ci-summary`: reads the downloaded results artifacts.
- `bug-report`: `gh issue create --title <t> --body-file <file>` (flags checked in `gh issue create --help`, 2.103.3), only when the user asks.
- `requirements-analysis`: `gh issue view <n>` to read a story, and `gh issue comment` only when the user asks.

**Rules carried into the skills:**
1. **Read results artifacts, not logs.** CI templates publish a small results artifact (TRX plus a summary JSON) on every run. Skills download it to a gitignored folder and run `Harness.Tools ci-summary` on it. Raw logs are a last resort: run them through the `suite-runner` agent, and only extract the failing step.
2. **Use `--json` with `ConvertFrom-Json`, never `--jq` or `--template` with embedded quotes.** This works on both 5.1 and pwsh 7.
3. **Read-only by default.** These actions need the user's explicit request, and `hook pre-shell` asks before running them:
   - `gh workflow run`
   - `gh run rerun` and `gh run cancel`
   - `gh pr create`, `gh pr merge` and `gh pr comment`
   - `gh issue create`, `gh issue comment`, `gh issue edit` and `gh issue close`
   - `gh release`
   - `gh api` with `-X POST`, `PUT`, `PATCH` or `DELETE`
   - `gh secret` and `gh variable`
4. **Missing or logged out:** the skill says so and stops. `harness-setup` checks `gh auth status` and reports the result without failing.

## Claude Code workflows

Documented at code.claude.com/docs/en/workflows.

- Saved workflows live in `.claude/workflows/*.js` as plain JavaScript.
- They use `agent()`, `pipeline()`, `parallel()` and `phase()`.
- They cannot import modules or access files directly.
- `Date.now()` and `Math.random()` throw inside a workflow.
- They are available on paid plans and can be turned off with `disableWorkflows`.
- In auto mode, prompts that a script passes to `agent()` don't count as user requests.

**Decision:**
- Workflows are allowed, but **optional**.
- Every workflow-driven loop must also work with workflows disabled, by having the main agent drive the subagents.
- The `Workflow` tool was not available in the vetting session, and it was still not available on the second check on 2026-11-07. **The live run is deferred** to the first session where the tool exists. Until then no workflow file is written, and the loops are driven by the main agent.
