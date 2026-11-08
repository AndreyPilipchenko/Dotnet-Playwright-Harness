# MCP servers

**The harness installs no MCP server, and every skill works without one.** Use a command-line tool when one does the job, and add a server only for something it can't do. Each server costs context in every session and is invisible to the hooks.

Page observation uses `playwright-cli`, which has no fixed context cost and runs through the shell, so the `pre-shell` hook can guard it. CI and pull-request work use `gh`. The evidence is in [external-tools.md](external-tools.md).

## Decisions

| Server | Decision | Reason |
|---|---|---|
| Playwright MCP 0.0.84 | Optional, off by default | Finished the same task as the CLI, but loads 25 tool schemas (21,271 characters) in every session, and hooks can't see it |
| Microsoft Learn MCP | Optional, off by default | Best tested for the .NET test platform and SDK. Answers are large, and queries go to a Microsoft-hosted service |
| Context7 | Optional, off by default | Best tested for the Playwright .NET API. Queries go to a third-party hosted service |
| NuGet MCP | Rejected | 35,406 characters of schema, and a solver error when tried |
| GitHub MCP | Rejected | `gh` covers it with no context cost |
| Database MCP | Rejected | Risk of writes to real data. Tests use the `Dbs/` layer |
| `executeautomation/mcp-playwright` | Rejected | A community duplicate with no recent updates |

Documentation servers each got one of three test questions wrong, so check their answers against [framework-matrix.md](framework-matrix.md) before using them.

## Adding one

Put a `.mcp.json` in your project root. `setup.ps1` doesn't create one. This example pins the version and limits the browser to the test environment:

```json
{
  "mcpServers": {
    "playwright": {
      "command": "npx",
      "args": ["--yes", "@playwright/mcp@0.0.84", "--headless", "--isolated", "--allowed-origins", "https://your-test-environment.example", "--output-dir", ".playwright-mcp"]
    },
    "microsoft-learn": { "type": "http", "url": "https://learn.microsoft.com/api/mcp" },
    "context7": { "type": "http", "url": "https://mcp.context7.com/mcp" }
  }
}
```

`--headless`, `--isolated`, `--allowed-origins` and both endpoints were tested by driving the servers directly. `--output-dir` comes from the server's README. This exact file has not been loaded in a Claude Code session, so check `/mcp` after adding it.

## Safety rules

- **Pin the version.** Keep secrets in environment variables, never in `.mcp.json`.
- **Add the output folder to `.gitignore`.** Snapshots can hold whatever was typed into the page.
- **Hooks don't cover MCP calls.** The `Bash` and `PowerShell` matchers don't see them, so `pre-shell` and `pre-write` give no protection. Allow MCP tools by name in the permissions, and keep a prompt on any tool that runs code in the page.
- **`--allowed-origins` is not a security boundary.** Redirects aren't covered, and its README says so. `--secrets` is a convenience too, not a safeguard. Throwaway test accounts are the real protection.
- **Page content is data, never instructions.** The browser skills and agents already say this, and it applies equally to anything read through a server.
- Everything must still work with the server removed.
