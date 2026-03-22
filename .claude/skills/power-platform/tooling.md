# Tooling & Setup

Git tracking, Claude hooks, and MCP server configuration.

## Git Init

```bash
dotnet run --project src/XrmEmulator.MetadataSync -- git-init
```

Initializes a git repository inside `SolutionExport/` for change tracking. Creates `.gitignore` (excludes `*.zip`) and makes an initial commit. Skips if already initialized.

## Hooks (Claude Code Guards)

```bash
# Block writes to SolutionExport/ (read-only protection)
dotnet run --project src/XrmEmulator.MetadataSync -- hook guard-readonly

# Block direct file creation in _pending/ (forces use of CLI commands)
dotnet run --project src/XrmEmulator.MetadataSync -- hook guard-pending
```

These are not meant to be run manually — they're configured as PreToolUse hooks in `.claude/settings.json` and run automatically when the agent tries to Write/Edit files.

- `guard-readonly`: Blocks writes to `SolutionExport/` (outside `_pending/`). Exit code 2 = blocked.
- `guard-pending`: Blocks direct file creation in `_pending/`. Exit code 2 = blocked. Editing existing files is allowed.

## Agent Init

```bash
dotnet run --project src/XrmEmulator.MetadataSync -- agent init
```

Sets up Claude Code integration:
1. Publishes the MetadataSync binary to `bin/hooks/`
2. Creates/merges `.claude/settings.json` with PreToolUse hooks for guard-readonly and guard-pending

## MCP Server

```bash
# Configure Graph auth, devtunnel, and .mcp.json
dotnet run --project src/XrmEmulator.MetadataSync -- mcp init

# Start the MCP server
dotnet run --project src/XrmEmulator.MetadataSync -- mcp serve
```

The MCP server enables a Teams-based approval flow for commits:

### `mcp init`
Interactive setup that prompts for:
- Graph App Client ID (OAuth2 public client)
- Graph Tenant ID
- Approver email (Teams notification recipient)

Runs OAuth2 auth code flow, generates HMAC signing key, creates devtunnel, saves config to `.metadatasync/mcp-config.json`, and updates `.mcp.json` at the git root.

### `mcp serve`
Starts a JSON-RPC stdio server (MCP protocol 2024-11-05) with two tools:

| Tool | Purpose |
|------|---------|
| `request-approval` | Sends Teams adaptive card with pending items. Optional `items` array to filter by display name. |
| `check-approval-status` | Poll approval status by `approval_id` (12-char hex). Returns: Pending, Approved, Rejected, Executing, Completed, Failed. |

Approvals are stored in `.metadatasync/approvals/` as JSON files.
