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

**Decision: pending.**
- Current recommendation: take the knowledge (the command and filter tables) into the `test-framework` and `run-tests` playbooks, verified against our own Phase 1 spikes.
- Separately, offer the plugin as an optional install together with an overlay file.
- Final decision comes after the Phase 1 spikes confirm the per-framework commands.

### `eng/skill-validator`

- **`check`** does static analysis of skills, plugins and agents, with no LLM involved.
- **`evaluate`** runs an A/B comparison with an LLM judge, but needs a GitHub Copilot token through the Copilot SDK, so it doesn't fit a Claude-only setup.
- Binaries are published only as `skill-validator-nightly` builds, which are mutable and can't be pinned properly.
- The source repo pins an SDK 11 preview, but the validator itself targets `net10.0`. It **built cleanly with SDK 10.0.403** in a scratch copy.
- Running the built binary was blocked by the session's permission classifier, so `check` has not been tried.

**Decision: pending.** Our own `Harness.Tools validate-skills` stays in the plan. `check` is an optional extra if a run proves it adds value.

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

**Decision: pending.** The token and task-success comparison still needs to be run.

## Documentation servers

| Candidate | Notes | Decision |
|---|---|---|
| Microsoft Learn MCP | Hosted HTTP endpoint. Sends search queries to Microsoft | pending (opt-in) |
| `@upstash/context7-mcp` 4.5.1 (MIT) | Hosted docs index. Also exists as a Claude plugin in this environment | pending (opt-in); compare with Learn for playwright.dev coverage |
| NuGet MCP | Needs `dnx` (SDK 10, now installed) | pending; expected to be rejected in favour of `dotnet list package --outdated --vulnerable` |

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
- The `Workflow` tool was not available in the session that ran this vetting, so a live run is still pending.
