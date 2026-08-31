using System.Text.Json;
using System.Text.RegularExpressions;
using CodepostEx.Models;
using Microsoft.Extensions.Logging;

namespace CodepostEx.Services;

public sealed class ChatExtractor
{
    private readonly VscdbService _vscdb;
    private readonly ProtobufReader _proto;
    private readonly ILogger<ChatExtractor> _log;

    // Same flat-object regex as the PS reference script — matches any JSON object
    // with no nested braces that contains at least one chat-related field name.
    private static readonly Regex ChatFragmentRegex = new(
        @"\{[^\{\}]*?""(?:message|content|text|prompt|response|user|assistant|ai)""[^\{\}]*?\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ItemTable keys that contain chat bubbles/turns
    private static readonly string[] ChatKeyPatterns =
    [
        "%chatdata%", "%bubble%", "%aichat%", "%composer%",
        "%conversation%", "%messageHistory%", "%aiSession%",
        "workbench.panel.aichat%", "%cursorAi%", "%aiGeneration%",
        "%cascadeMessages%", "%chatHistory%", "%agentSession%",
        "aiService.%", "composer.%",
        // Cursor Glass/reactive storage
        "src.vs.platform.reactivestorage%",
        "cursor/glass%", "glass.%",
        "%cursorDiskKV%", "cursorDiskKV",
    ];

    public ChatExtractor(VscdbService vscdb, ProtobufReader proto, ILogger<ChatExtractor> log)
    {
        _vscdb = vscdb;
        _proto = proto;
        _log   = log;
    }

    public IReadOnlyList<ChatEntry> Extract(IdeTarget target, DateTime? since)
    {
        return target.Profile.StorageType switch
        {
            IdeStorageType.Sqlite           => ExtractSqlite(target, since),
            IdeStorageType.Protobuf         => ExtractProtobuf(target, since),
            IdeStorageType.AntigravityJsonl => ExtractAntigravityJsonl(target, since),
            _                               => [],
        };
    }

    // ── SQLite ────────────────────────────────────────────────────────────

    private IReadOnlyList<ChatEntry> ExtractSqlite(IdeTarget target, DateTime? since)
    {
        var results = new List<ChatEntry>();

        // global storage
        ExtractFromDb(target.GlobalStoragePath, "global", since, results);

        // per-workspace storage
        if (target.WorkspaceStoragePath is not null
            && Directory.Exists(target.WorkspaceStoragePath))
        {
            foreach (var wsDir in Directory.EnumerateDirectories(target.WorkspaceStoragePath))
            {
                var dbPath = Path.Combine(wsDir, "state.vscdb");
                if (File.Exists(dbPath))
                    ExtractFromDb(dbPath, Path.GetFileName(wsDir), since, results);
            }
        }

        return results;
    }

    private void ExtractFromDb(
        string dbPath, string workspaceHash, DateTime? since, List<ChatEntry> results)
    {
        using var conn = _vscdb.OpenReadOnly(dbPath);
        if (conn is null) return;

        var seenValues = new HashSet<string>(StringComparer.Ordinal);
        bool dbg = Environment.GetEnvironmentVariable("CODEPOST_VERBOSE") == "1";
        int patternHits = 0;

        // Track count BEFORE this DB so fallback decisions are per-DB, not global.
        int countBefore = results.Count;

        // Single query — 18 LIKE round-trips replaced with one GetAllTextRows + C# key filter.
        var allRows = VscdbService.GetAllTextRows(conn).ToList();

        foreach (var (key, value) in allRows)
        {
            if (!MatchesChatKey(key)) continue;
            patternHits++;
            if (dbg) Console.Error.WriteLine($"[dbg] key={key} len={value.Length} pre={value[..Math.Min(80, value.Length)]}");
            if (!seenValues.Add(value)) continue;
            if (!value.StartsWith('{') && !value.StartsWith('[')) continue;
            var entries = ParseChatJson(value, workspaceHash, since);
            results.AddRange(entries);
        }

        // Fallback: all rows not matched by key pattern (catches uncommon key names).
        // Use per-DB delta, not global count — avoids skipping workspaces after global DB fills results.
        if (results.Count == countBefore)
        {
            int fallbackRows = 0;
            foreach (var (key, value) in allRows)
            {
                fallbackRows++;
                if (dbg) Console.Error.WriteLine($"[dbg-fb] key={key} len={value.Length}");
                if (!seenValues.Add(value)) continue;
                if (!value.StartsWith('{') && !value.StartsWith('[')) continue;
                var entries = ParseChatJson(value, workspaceHash, since);
                results.AddRange(entries);
            }
            if (dbg) Console.Error.WriteLine($"[dbg] fallback scanned {fallbackRows} rows in {Path.GetFileName(dbPath)}");
        }

        // Raw binary scan — mirrors PS script approach: read .vscdb as UTF-8, regex flat JSON fragments.
        if (results.Count == countBefore)
            ExtractRawBinary(dbPath, workspaceHash, since, results, dbg);

        if (dbg) Console.Error.WriteLine($"[dbg] {dbPath}: patternHits={patternHits} delta={results.Count - countBefore}");
    }

    private static bool MatchesChatKey(string key) =>
        key.Contains("chatdata",        StringComparison.OrdinalIgnoreCase) ||
        key.Contains("bubble",          StringComparison.OrdinalIgnoreCase) ||
        key.Contains("aichat",          StringComparison.OrdinalIgnoreCase) ||
        key.Contains("composer",        StringComparison.OrdinalIgnoreCase) ||
        key.Contains("conversation",    StringComparison.OrdinalIgnoreCase) ||
        key.Contains("messageHistory",  StringComparison.OrdinalIgnoreCase) ||
        key.Contains("aiSession",       StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("workbench.panel.aichat", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cursorAi",        StringComparison.OrdinalIgnoreCase) ||
        key.Contains("aiGeneration",    StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cascadeMessages", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("chatHistory",     StringComparison.OrdinalIgnoreCase) ||
        key.Contains("agentSession",    StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("aiService.",    StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("composer.",     StringComparison.OrdinalIgnoreCase) ||
        key.Contains("reactivestorage", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("cursor/glass",  StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("glass.",        StringComparison.OrdinalIgnoreCase) ||
        key.Contains("cursorDiskKV",    StringComparison.OrdinalIgnoreCase) ||
        // Kiro / Amazon Q
        key.Contains("kiro",            StringComparison.OrdinalIgnoreCase) ||
        key.Contains("amazonq",         StringComparison.OrdinalIgnoreCase) ||
        key.Contains("amazon.q",        StringComparison.OrdinalIgnoreCase) ||
        key.Contains("chatSession",     StringComparison.OrdinalIgnoreCase) ||
        key.Contains("sessionState",    StringComparison.OrdinalIgnoreCase) ||
        // Trae (ByteDance)
        key.Contains("trae",            StringComparison.OrdinalIgnoreCase);

    private static void ExtractRawBinary(
        string dbPath, string workspaceHash, DateTime? since, List<ChatEntry> results, bool dbg)
    {
        string? rawText = null;
        string? tempPath = null;

        try { rawText = File.ReadAllText(dbPath, System.Text.Encoding.UTF8); }
        catch (IOException)
        {
            tempPath = Path.Combine(Path.GetTempPath(), $"cp_raw_{Guid.NewGuid():N}.vscdb");
            try
            {
                File.Copy(dbPath, tempPath, overwrite: true);
                rawText = File.ReadAllText(tempPath, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        if (rawText is null)
        {
            if (tempPath is not null) try { File.Delete(tempPath); } catch { }
            return;
        }

        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int hits = 0;

            foreach (Match m in ChatFragmentRegex.Matches(rawText))
            {
                var fragment = m.Value;
                if (!seen.Add(fragment)) continue;
                hits++;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(fragment, new JsonDocumentOptions { AllowTrailingCommas = true }); }
                catch { continue; }

                using (doc)
                {
                    var root    = doc.RootElement;
                    var userMsg = GetStringField(root, "message", "text", "prompt", "user", "content");
                    var aiResp  = GetStringField(root, "response", "assistant", "ai");

                    if (string.IsNullOrWhiteSpace(userMsg) && string.IsNullOrWhiteSpace(aiResp))
                        continue;

                    var ts = GetTimestamp(root);
                    if (since != null && ts != null && ts.Value.UtcDateTime < since) continue;

                    results.Add(new ChatEntry(
                        WorkspaceHash: workspaceHash,
                        FolderUri:     null,
                        Id:            Guid.NewGuid().ToString(),
                        Timestamp:     ts,
                        UserMessage:   userMsg ?? "",
                        AiResponse:    aiResp ?? "",
                        IsSensitive:   false));
                }
            }

            if (dbg) Console.Error.WriteLine($"[dbg-raw] {Path.GetFileName(dbPath)}: hits={hits} new-results={results.Count}");
        }
        finally
        {
            if (tempPath is not null) try { File.Delete(tempPath); } catch { }
        }
    }

    // ── Antigravity JSONL ─────────────────────────────────────────────────

    private IReadOnlyList<ChatEntry> ExtractAntigravityJsonl(IdeTarget target, DateTime? since)
    {
        var brainDir = target.Profile.AbsoluteChatStoragePath;
        if (brainDir is null || !Directory.Exists(brainDir)) return [];

        var results = new List<ChatEntry>();
        foreach (var convDir in Directory.EnumerateDirectories(brainDir))
        {
            var uuid           = Path.GetFileName(convDir)!;
            var transcriptPath = Path.Combine(convDir, ".system_generated", "logs", "transcript_full.jsonl");
            if (!File.Exists(transcriptPath)) continue;

            var (conversationLabel, workspacePath) = AntigravityConversationTitle(transcriptPath);
            if (string.IsNullOrWhiteSpace(conversationLabel))
                conversationLabel = uuid[..Math.Min(8, uuid.Length)];

            try
            {
                foreach (var entry in ParseAntigravityTranscript(transcriptPath, uuid, workspacePath, since))
                    results.Add(entry);
            }
            catch (Exception ex)
            {
                _log.LogDebug("Antigravity transcript error {path}: {msg}", transcriptPath, ex.Message);
            }
        }
        return results;
    }

    private static IEnumerable<ChatEntry> ParseAntigravityTranscript(
        string path, string conversationId, string? workspacePath, DateTime? since)
    {
        string? pendingUser = null;
        DateTimeOffset? pendingTs = null;

        foreach (var line in File.ReadLines(path, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }

            using (doc)
            {
                var root   = doc.RootElement;
                var source = AntiStr(root, "source");
                var type   = AntiStr(root, "type");

                DateTimeOffset? ts = null;
                var caStr = AntiStr(root, "created_at");
                if (caStr is not null && DateTimeOffset.TryParse(caStr, out var pts)) ts = pts;

                if (source == "USER_EXPLICIT" && type == "USER_INPUT")
                {
                    var content = AntigravityContent(root);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        pendingUser = content;
                        pendingTs   = ts;
                    }
                }
                else if (source == "MODEL" && type == "PLANNER_RESPONSE" && pendingUser is not null)
                {
                    var aiContent = AntigravityContent(root);
                    if (!string.IsNullOrWhiteSpace(aiContent))
                    {
                        var entryTs = pendingTs ?? ts;
                        if (since == null || entryTs == null || entryTs.Value.UtcDateTime >= since)
                        {
                            yield return new ChatEntry(
                                WorkspaceHash: conversationId,
                                FolderUri:     workspacePath,
                                Id:            Guid.NewGuid().ToString(),
                                Timestamp:     entryTs,
                                UserMessage:   pendingUser,
                                AiResponse:    aiContent,
                                IsSensitive:   false);
                        }
                        pendingUser = null;
                        pendingTs   = null;
                    }
                }
            }
        }

        // Trailing unpaired user message
        if (pendingUser is not null)
        {
            if (since == null || pendingTs == null || pendingTs.Value.UtcDateTime >= since)
            {
                yield return new ChatEntry(
                    WorkspaceHash: conversationId,
                    FolderUri:     workspacePath,
                    Id:            Guid.NewGuid().ToString(),
                    Timestamp:     pendingTs,
                    UserMessage:   pendingUser,
                    AiResponse:    "",
                    IsSensitive:   false);
            }
        }
    }

    private static string? AntigravityContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var cp) || cp.ValueKind != JsonValueKind.String)
            return null;
        var s = cp.GetString() ?? "";
        s = StripXmlBlock(s, "USER_REQUEST");
        s = StripXmlBlock(s, "ADDITIONAL_METADATA");
        return s.Trim().Length == 0 ? null : s.Trim();
    }

    private static string StripXmlBlock(string s, string tag)
    {
        var open  = "<" + tag + ">";
        var close = "</" + tag + ">";
        int start = s.IndexOf(open,  StringComparison.OrdinalIgnoreCase);
        int end   = s.IndexOf(close, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            s = (s[..start] + s[(end + close.Length)..]).Trim();
        return s;
    }

    private static string? AntiStr(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString();
        return null;
    }

    private static (string? label, string? fullPath) AntigravityConversationTitle(string transcriptPath)
    {
        // Prefer workspace folder derived from the first tool call path argument.
        // Fall back to first user message snippet.
        string? userFallback = null;

        foreach (var line in File.ReadLines(transcriptPath, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root   = doc.RootElement;
                var source = AntiStr(root, "source");
                var type   = AntiStr(root, "type");

                // Capture first user message as fallback
                if (userFallback is null && source == "USER_EXPLICIT" && type == "USER_INPUT")
                {
                    var content = AntigravityContent(root);
                    if (!string.IsNullOrWhiteSpace(content))
                        userFallback = content.Replace('\n', ' ').Replace('\r', ' ').Trim();
                }

                // Extract workspace folder from first tool call with a path argument
                if (source == "MODEL" && type == "PLANNER_RESPONSE"
                    && root.TryGetProperty("tool_calls", out var calls)
                    && calls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var call in calls.EnumerateArray())
                    {
                        if (!call.TryGetProperty("args", out var args)) continue;
                        foreach (var prop in args.EnumerateObject())
                        {
                            var val = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString() : null;
                            if (val is null) continue;
                            // Any argument that looks like an absolute path
                            if ((val.Length > 3 && val[1] == ':') || val.StartsWith('/'))
                            {
                                var name = Path.GetFileName(val.TrimEnd('/', '\\'));
                                if (!string.IsNullOrWhiteSpace(name))
                                    return (name, val);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return (userFallback, null);
    }

    // ── Protobuf ──────────────────────────────────────────────────────────

    private IReadOnlyList<ChatEntry> ExtractProtobuf(IdeTarget target, DateTime? since)
    {
        var chatDir = target.Profile.AbsoluteChatStoragePath;
        if (chatDir is null || !Directory.Exists(chatDir)) return [];

        var results = new List<ChatEntry>();
        foreach (var pbFile in Directory.EnumerateFiles(chatDir, "*.pb"))
        {
            var entries = _proto.ExtractChats(pbFile, "protobuf", since);
            results.AddRange(entries);
        }
        return results;
    }

    // ── JSON parser ───────────────────────────────────────────────────────

    private static IEnumerable<ChatEntry> ParseChatJson(
        string json, string workspaceHash, DateTime? since)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 64 });
        }
        catch { yield break; }

        using (doc)
        {
            foreach (var entry in WalkElement(doc.RootElement, workspaceHash, since))
                yield return entry;
        }
    }

    private static readonly string[] WorkspaceFolderFields =
    [
        "workspaceRootPath", "workspacePath", "workspaceFolder",
        "folderUri", "rootPath", "projectRootPath", "projectPath",
        "cwd", "workingDirectory", "absoluteRootPath",
        "tabWorkspacePath", "currentWorkspacePath",
    ];

    private static string? GetWorkspaceFolder(JsonElement el)
    {
        foreach (var f in WorkspaceFolderFields)
        {
            if (el.TryGetProperty(f, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (!string.IsNullOrWhiteSpace(s) && (s.Contains('/') || s.Contains('\\')))
                    return s;
            }
        }

        // Cursor: "context" may contain file URIs — extract workspace root from first file reference
        if (el.TryGetProperty("context", out var ctx))
        {
            var uri = ExtractFirstFileUri(ctx);
            if (uri != null) return uri;
        }

        // Cursor: "relevantFiles" or "mentionedFiles" arrays
        foreach (var arrayField in new[] { "relevantFiles", "mentionedFiles", "files" })
        {
            if (el.TryGetProperty(arrayField, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var uri = ExtractFirstFileUri(arr);
                if (uri != null) return uri;
            }
        }

        return null;
    }

    private static string? ExtractFirstFileUri(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (s != null && s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                        return ExtractDirFromUri(s);
                }
                if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in new[] { "uri", "path", "filePath", "fileName" })
                    {
                        if (item.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String)
                        {
                            var s = p.GetString();
                            if (s != null && (s.Contains('/') || s.Contains('\\')))
                                return ExtractDirFromUri(s);
                        }
                    }
                }
            }
        }
        return null;
    }

    private static string? ExtractDirFromUri(string uri)
    {
        try
        {
            var p = uri;
            if (p.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                p = Uri.UnescapeDataString(p[8..]).Replace('/', Path.DirectorySeparatorChar);
            var dir = Path.GetDirectoryName(p);
            return string.IsNullOrEmpty(dir) ? null : dir;
        }
        catch { return null; }
    }

    private static IEnumerable<ChatEntry> WalkElement(
        JsonElement el, string workspace, DateTime? since, string? folderUri = null)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            var localFolder = GetWorkspaceFolder(el) ?? folderUri;

            // Check if this object IS a bubble/turn
            if (el.TryGetProperty("type", out var typeProp)
                || el.TryGetProperty("role", out typeProp))
            {
                bool isUser, isAi;
                if (typeProp.ValueKind == JsonValueKind.String)
                {
                    var type = typeProp.GetString() ?? "";
                    isUser = type is "user" or "human";
                    isAi   = type is "ai" or "assistant" or "model" or "bot";
                }
                else if (typeProp.ValueKind == JsonValueKind.Number
                    && typeProp.TryGetInt32(out var typeNum))
                {
                    // Cursor composer format: 1 = human/user, 2 = ai
                    isUser = typeNum == 1;
                    isAi   = typeNum == 2;
                }
                else { isUser = false; isAi = false; }

                if (isUser || isAi)
                {
                    var text = GetStringField(el,
                        "text", "rawText", "content", "message", "response");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var ts = GetTimestamp(el);
                        if (since == null || ts == null || ts.Value.UtcDateTime >= since)
                        {
                            yield return new ChatEntry(
                                WorkspaceHash: workspace,
                                FolderUri:     localFolder,
                                Id:            Guid.NewGuid().ToString(),
                                Timestamp:     ts,
                                UserMessage:   isUser ? text : "",
                                AiResponse:    isAi  ? text : "",
                                IsSensitive:   false);
                            yield break;
                        }
                    }
                }
            }

            // Flat pair: object has user-side + AI-side fields directly (no type/role)
            // Catches Kiro/Amazon Q style: {"user":"...","assistant":"..."} or similar
            if (!el.TryGetProperty("type", out _) && !el.TryGetProperty("role", out _))
            {
                var userFlat = GetStringField(el, "user", "human", "userMessage", "input", "query");
                var aiFlat   = GetStringField(el, "assistant", "ai", "response", "output", "answer", "completion");
                if (!string.IsNullOrWhiteSpace(userFlat) && userFlat.Length > 5 &&
                    !string.IsNullOrWhiteSpace(aiFlat)   && aiFlat.Length > 5)
                {
                    var ts = GetTimestamp(el);
                    if (since == null || ts == null || ts.Value.UtcDateTime >= since)
                    {
                        yield return new ChatEntry(
                            WorkspaceHash: workspace,
                            FolderUri:     localFolder,
                            Id:            Guid.NewGuid().ToString(),
                            Timestamp:     ts,
                            UserMessage:   userFlat,
                            AiResponse:    aiFlat,
                            IsSensitive:   false);
                        yield break;
                    }
                }
            }

            // Recurse into all properties, propagating workspace context
            foreach (var prop in el.EnumerateObject())
                foreach (var entry in WalkElement(prop.Value, workspace, since, localFolder))
                    yield return entry;
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                foreach (var entry in WalkElement(item, workspace, since, folderUri))
                    yield return entry;
        }
    }

    private static string? GetStringField(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var prop)) continue;

            if (prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            else if (prop.ValueKind == JsonValueKind.Array)
            {
                // Anthropic messages format: content: [{type:"text", text:"..."}]
                var parts = new System.Text.StringBuilder();
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            if (parts.Length > 0) parts.Append('\n');
                            parts.Append(s);
                        }
                    }
                }
                if (parts.Length > 0) return parts.ToString();
            }
        }
        return null;
    }

    private static readonly string[] TimestampFieldNames =
        ["timestamp", "createdAt", "created_at", "time"];

    private static DateTimeOffset? GetTimestamp(JsonElement el)
    {
        foreach (var name in TimestampFieldNames)
        {
            if (!el.TryGetProperty(name, out var prop)) continue;

            if (prop.ValueKind == JsonValueKind.Number
                && prop.TryGetInt64(out var ms))
            {
                // Unix ms
                try { return DateTimeOffset.FromUnixTimeMilliseconds(ms); } catch { }
                // Unix seconds
                try { return DateTimeOffset.FromUnixTimeSeconds(ms); } catch { }
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (s is not null && DateTimeOffset.TryParse(s, out var dt))
                    return dt;
            }
        }
        return null;
    }
}
