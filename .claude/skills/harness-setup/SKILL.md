---
name: harness-setup
description: Use when installing, updating, repairing, configuring or rolling back this Playwright C# harness in a project - "install the harness", "update the harness", "the skills or hooks are missing", ".harness-new files appeared", setting the application URL or secret names, or checking that a machine is ready after cloning a project.
---

# Harness setup

Install, update and repair go through the two scripts, which are safe to re-run. The procedure is in
[the playbook](references/playbook.md).

| The request | Section |
|---|---|
| Install into a project, or set up after cloning | [1. Install](references/playbook.md#1-install) |
| Update to a newer harness; `.harness-new` files | [2. Update](references/playbook.md#2-update) |
| The application URL, secrets, optional tools | [3. Configure](references/playbook.md#3-configure) |
| "Is this machine ready?" | [4. Check readiness](references/playbook.md#4-check-readiness) |
| Undo an install or update | [5. Roll back](references/playbook.md#5-roll-back) |

## Rules

- **Never copy payload files by hand.** The scripts track what they installed in `.harness/manifest.json`, and that is how local changes stay safe.
- **Never ask for, read, print or write a secret value.** Name the variables, and let the user set them.
- **Report conflicts as they are** and ask the user. Never resolve one by deleting or overwriting a file you haven't compared.
- **Restart Claude Code after setup or update.** Agents and hooks load when it starts.
- Commit nothing. Show `git status --short`, and suggest a commit message.

## Boundaries

- Creating the framework itself after install → scaffold-framework
- Packages, analyzers and build settings → csharp-standards
- Where config and secrets are read in code → utility-classes
