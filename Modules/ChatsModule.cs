using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Output;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class ChatsModule
{
    public static async Task RunAsync(
        string ide, DateTime? since, bool html, string output, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var paths     = sp.GetRequiredService<PathResolver>();
        var extractor = sp.GetRequiredService<ChatExtractor>();

        Directory.CreateDirectory(Path.Combine(output, "AIChats"));

        foreach (var profile in IdeProfiles.Filter(ide))
        {
            var target = discovery.Resolve(profile, paths);
            if (target is null) { Out.Minus($"{profile.Name}: not detected, skipping."); continue; }

            Out.Star($"Extracting chats from {target.Name}...");

            var jsonPath = OutputPaths.ChatsJson(output, target.Name);
            var txtPath  = OutputPaths.ChatsTxt(output,  target.Name);

            if (File.Exists(jsonPath) && !html)
            {
                Out.Star($"Already extracted: {jsonPath}");
                continue;
            }

            var chats = extractor.Extract(target, since);

            if (chats.Count == 0)
            {
                Out.Minus($"{target.Name}: no chats found.");
                continue;
            }

            Out.Plus($"{target.Name}: {chats.Count} entries extracted.");

            var wsMap = BuildWorkspaceMap(target);

            // Remap "global" chats to actual workspaces using FolderUri
            chats = RemapGlobalChats(chats, wsMap);

            var wsCount = chats.Select(c => c.WorkspaceHash).Distinct().Count();
            Out.Star($"Mapped to {wsCount} workspace(s).");

            // -- JSON --------------------------------------------------
            var groups = chats
                .GroupBy(c => c.WorkspaceHash)
                .Select(g => new
                {
                    Workspace = g.Key,
                    Folder    = wsMap.TryGetValue(g.Key, out var f) ? f : (g.First().FolderUri ?? g.Key),
                    Chats     = g.ToList(),
                })
                .OrderBy(g => g.Workspace)
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            await File.WriteAllTextAsync(jsonPath,
                JsonSerializer.Serialize(groups,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    }),
                Encoding.UTF8);
            Out.Plus($"JSON: {jsonPath}");

            // -- TXT ---------------------------------------------------
            var sb = new StringBuilder();
            foreach (var chat in chats.Where(c =>
                !string.IsNullOrWhiteSpace(c.UserMessage) ||
                !string.IsNullOrWhiteSpace(c.AiResponse)))
            {
                var folder = wsMap.TryGetValue(chat.WorkspaceHash, out var wf) ? wf : (chat.FolderUri ?? "");
                sb.AppendLine(new string('=', 80));
                sb.AppendLine($"Chat ID  : {chat.Id}");
                sb.AppendLine($"Timestamp: {chat.Timestamp?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "unknown"}");
                sb.AppendLine($"Workspace: {chat.WorkspaceHash}");
                if (!string.IsNullOrEmpty(folder)) sb.AppendLine($"Folder   : {folder}");
                if (chat.IsSensitive)               sb.AppendLine("*** CONTAINS SENSITIVE KEYWORDS ***");
                sb.AppendLine(new string('-', 80));
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(chat.UserMessage))
                {
                    sb.AppendLine("USER:");
                    sb.AppendLine(chat.UserMessage);
                    sb.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(chat.AiResponse))
                {
                    sb.AppendLine("AI:");
                    sb.AppendLine(chat.AiResponse);
                    sb.AppendLine();
                }
            }
            await File.WriteAllTextAsync(txtPath, sb.ToString(), Encoding.UTF8);
            Out.Plus($"TXT : {txtPath}");

            // -- HTML (single self-contained file) -----------------------
            if (html)
            {
                var htmlPath    = OutputPaths.ChatsHtml(output, target.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
                var wsFiles     = ReadArtifactFiles(output, target.Name, wsMap);
                var totalArtifacts = wsFiles.Values.Sum(v => v.Count);
                if (totalArtifacts > 0)
                    Out.Plus($"Artifact files loaded: {totalArtifacts}");
                else if (target.Profile.HistorySubPath is not null)
                    Out.Minus($"No artifact files found in ZIP — run -artifacts first if needed.");

                // For Antigravity, load html_artifacts keyed by conversation UUID
                if (target.Profile.StorageType == IdeStorageType.AntigravityJsonl)
                {
                    var antigravArtifacts = ReadAntigravityArtifactsByConversation(target);
                    int agCount = antigravArtifacts.Values.Sum(v => v.Count);
                    if (agCount > 0)
                    {
                        Out.Plus($"Antigravity html_artifacts loaded: {agCount}");
                        foreach (var (uuid, files) in antigravArtifacts)
                        {
                            if (!wsFiles.TryGetValue(uuid, out var existing))
                                wsFiles[uuid] = files;
                            else
                                existing.AddRange(files);
                        }
                    }
                    else
                    {
                        Out.Minus($"No Antigravity html_artifacts found.");
                    }
                }

                var globalFiles = wsFiles.TryGetValue("global", out var gf) ? gf : [];

                var workspaceData = groups.Select(g => new
                {
                    WorkspaceID = g.Workspace,
                    FolderPath  = g.Folder,
                    Chats       = g.Chats.Select(c => new
                    {
                        c.Id,
                        Timestamp   = c.Timestamp?.ToString("yyyy-MM-ddTHH:mm:ss"),
                        c.UserMessage,
                        c.AiResponse,
                        c.IsSensitive,
                    }),
                    Files = wsFiles.TryGetValue(g.Workspace, out var files) ? files : new List<WorkspaceFileEntry>(),
                }).ToList();

                // Append unmatched (global) files as their own pseudo-workspace only if present
                object[] allData = globalFiles.Count > 0
                    ? [..workspaceData, new { WorkspaceID = "global", FolderPath = "(uncategorised)", Chats = Array.Empty<object>(), Files = globalFiles }]
                    : [..workspaceData];

                var jsonData   = JsonSerializer.Serialize(allData, new JsonSerializerOptions { WriteIndented = false });
                var base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                var template   = AssetLoader.Load("template.html");
                var finalHtml  = template
                    .Replace("{{TOOL_NAME}}",      target.Name)
                    .Replace("{{GENERATED_DATE}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Replace("{{CHAT_DATA_JSON}}", base64Data);

                await File.WriteAllTextAsync(htmlPath, finalHtml, Encoding.UTF8);
                Out.Plus($"HTML: {htmlPath}");
            }
        }
    }

    // ── Artifact-file helpers ─────────────────────────────────────────────

    private static Dictionary<string, List<WorkspaceFileEntry>> ReadArtifactFiles(
        string output, string toolName, Dictionary<string, string> wsMap)
    {
        var result = new Dictionary<string, List<WorkspaceFileEntry>>(StringComparer.OrdinalIgnoreCase);

        var artifactsDir = Path.Combine(output, "Artifacts");
        if (!Directory.Exists(artifactsDir)) return result;

        var zips = Directory.GetFiles(artifactsDir, $"{toolName}_History_*.zip", SearchOption.TopDirectoryOnly);
        if (zips.Length == 0) return result;

        var zipPath = zips.OrderByDescending(z => z).First();

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Group ZIP entries by their top-level directory (history hash).
            // ZipArchiveEntry.FullName uses '\' on Windows (from Path.GetRelativePath) — handle both.
            var byHash = archive.Entries
                .Where(e => e.FullName.Contains('/') || e.FullName.Contains('\\'))
                .GroupBy(e => e.FullName.Split(new[] { '/', '\\' }, 2)[0])
                .ToDictionary(g => g.Key, g => g.ToList());

            int dbgDirs = 0, dbgNoMeta = 0, dbgNoRes = 0, dbgNoId = 0,
                dbgNoContent = 0, dbgTooBig = 0, dbgNotText = 0, dbgAdded = 0;

            foreach (var (hash, entries) in byHash)
            {
                dbgDirs++;
                var metaEntry = entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals("entries.json", StringComparison.OrdinalIgnoreCase));
                if (metaEntry is null) { dbgNoMeta++; continue; }

                string resource;
                string? latestId = null;
                long?   latestTs = null;

                try
                {
                    using var s   = metaEntry.Open();
                    using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true });
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("resource", out var resProp)) { dbgNoRes++; continue; }
                    resource = resProp.GetString() ?? "";
                    if (string.IsNullOrEmpty(resource)) { dbgNoRes++; continue; }

                    if (root.TryGetProperty("entries", out var ents))
                    {
                        foreach (var ent in ents.EnumerateArray())
                        {
                            var id = ent.TryGetProperty("id", out var ip) ? ip.GetString() : null;
                            if (id is null) continue;
                            long ts = 0;
                            if (ent.TryGetProperty("timestamp", out var tp)) tp.TryGetInt64(out ts);
                            if (latestId is null || ts > (latestTs ?? 0))
                            {
                                latestTs = ts;
                                latestId = id;
                            }
                        }
                    }
                }
                catch { continue; }

                if (latestId is null) { dbgNoId++; continue; }

                var contentEntry = entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals(latestId, StringComparison.OrdinalIgnoreCase));
                if (contentEntry is null) { dbgNoContent++; continue; }
                if (contentEntry.Length > 200_000) { dbgTooBig++; continue; }

                string content;
                try
                {
                    // Use GetBuffer()+Length to avoid the extra ToArray() copy.
                    using var ms = new MemoryStream((int)contentEntry.Length);
                    using (var s = contentEntry.Open()) s.CopyTo(ms);
                    var buf = ms.GetBuffer();
                    var len = (int)ms.Length;
                    if (!IsTextContent(buf, len)) { dbgNotText++; continue; }
                    content = Encoding.UTF8.GetString(buf, 0, len);
                }
                catch { continue; }

                var filePath = resource;
                if (filePath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    filePath = Uri.UnescapeDataString(filePath[8..]).Replace('/', Path.DirectorySeparatorChar);

                var wsHash = MatchWorkspace(filePath, wsMap);
                if (!result.TryGetValue(wsHash, out var list))
                    result[wsHash] = list = [];

                list.Add(new WorkspaceFileEntry(
                    Path:      filePath,
                    Content:   content.Length > 100_000 ? content[..100_000] : content,
                    Language:  LangFromExt(filePath),
                    Size:      contentEntry.Length,
                    Timestamp: latestTs
                ));
                dbgAdded++;
            }
            Out.Star($"ZIP scan: {dbgDirs} dirs | noMeta={dbgNoMeta} noRes={dbgNoRes} noId={dbgNoId} noContent={dbgNoContent} tooBig={dbgTooBig} notText={dbgNotText} added={dbgAdded}");
        }
        catch (Exception ex)
        {
            Out.Warn($"Artifact ZIP read failed: {ex.Message}");
        }

        return result;
    }

    private static string MatchWorkspace(string filePath, Dictionary<string, string> wsMap)
    {
        foreach (var (hash, folder) in wsMap)
            if (!string.IsNullOrEmpty(folder) &&
                filePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return hash;
        return "global";
    }

    private static bool IsTextContent(byte[] bytes, int length = -1)
    {
        if (length < 0) length = bytes.Length;
        if (length == 0) return true;
        int check = Math.Min(length, 2048);
        int nulls = 0;
        for (int i = 0; i < check; i++)
            if (bytes[i] == 0) nulls++;
        return nulls < check / 20;
    }

    private static Dictionary<string, List<WorkspaceFileEntry>> ReadAntigravityArtifactsByConversation(IdeTarget target)
    {
        var result  = new Dictionary<string, List<WorkspaceFileEntry>>(StringComparer.OrdinalIgnoreCase);
        var brainDir = target.Profile.AbsoluteChatStoragePath;
        if (brainDir is null || !Directory.Exists(brainDir)) return result;

        // Check flat brain/html_artifacts/ first (some versions)
        var flat = Path.Combine(brainDir, "html_artifacts");
        if (Directory.Exists(flat))
            CollectHtmlArtifacts(flat, "global", result);

        // Also scan per-UUID subdirectories: brain/{uuid}/html_artifacts/
        //                               and: brain/{uuid}/.system_generated/html_artifacts/
        foreach (var convDir in Directory.EnumerateDirectories(brainDir))
        {
            var uuid = Path.GetFileName(convDir);
            var candidates = new[]
            {
                Path.Combine(convDir, "html_artifacts"),
                Path.Combine(convDir, ".system_generated", "html_artifacts"),
            };
            foreach (var dir in candidates)
            {
                if (!Directory.Exists(dir)) continue;
                CollectHtmlArtifacts(dir, uuid, result);
                break;
            }
        }

        return result;
    }

    private static void CollectHtmlArtifacts(
        string dir, string key, Dictionary<string, List<WorkspaceFileEntry>> result)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.html"))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 500_000) continue;
                var content = File.ReadAllText(file, Encoding.UTF8);
                if (!result.TryGetValue(key, out var list))
                    result[key] = list = [];
                list.Add(new WorkspaceFileEntry(
                    Path:      file,
                    Content:   content.Length > 100_000 ? content[..100_000] : content,
                    Language:  "html",
                    Size:      info.Length,
                    Timestamp: null));
            }
            catch { }
        }
    }

    private static string? LangFromExt(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ts" or ".tsx"                   => "typescript",
            ".js" or ".jsx" or ".mjs"         => "javascript",
            ".py"                             => "python",
            ".cs"                             => "csharp",
            ".go"                             => "go",
            ".rs"                             => "rust",
            ".java"                           => "java",
            ".cpp" or ".cc" or ".cxx"         => "cpp",
            ".c"                              => "c",
            ".h" or ".hpp"                    => "cpp",
            ".sql"                            => "sql",
            ".sh" or ".bash"                  => "bash",
            ".ps1" or ".psm1"                 => "powershell",
            ".json"                           => "json",
            ".yaml" or ".yml"                 => "yaml",
            ".xml"                            => "xml",
            ".html" or ".htm"                 => "html",
            ".css" or ".scss" or ".sass"      => "css",
            ".md" or ".markdown"              => "markdown",
            ".rb"                             => "ruby",
            ".php"                            => "php",
            ".swift"                          => "swift",
            ".kt" or ".kts"                   => "kotlin",
            ".tf" or ".tfvars"                => "terraform",
            ".r"                              => "r",
            ".lua"                            => "lua",
            ".txt"                            => "text",
            _                                 => null,
        };

    // Matches absolute Windows paths: C:\... or C:/...
    private static readonly Regex WinPathRegex = new(
        @"[A-Za-z]:[\\\/][^\s""'<>|?*\x00-\x1f\\\/][^\s""'<>|?*\x00-\x1f]*",
        RegexOptions.Compiled);

    private static IReadOnlyList<ChatEntry> RemapGlobalChats(
        IReadOnlyList<ChatEntry> chats, Dictionary<string, string> wsMap)
    {
        // Build reverse map: normalized folder path → workspace hash
        // Sort longest-first so most-specific path wins on prefix match
        var pairs = wsMap
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => (Hash: kv.Key, Folder: NormalizePath(kv.Value)))
            .OrderByDescending(x => x.Folder.Length)
            .ToList();

        if (pairs.Count == 0) return chats;

        var result = new List<ChatEntry>(chats.Count);
        foreach (var c in chats)
        {
            if (c.WorkspaceHash != "global")
            {
                result.Add(c);
                continue;
            }

            string? matched = null;

            // 1. FolderUri from extracted JSON
            if (!string.IsNullOrEmpty(c.FolderUri))
                matched = BestPrefix(NormalizePath(c.FolderUri), pairs);

            // 2. Content scan: find Windows paths in chat text, match to workspace
            if (matched == null)
                matched = FindWorkspaceInText(c.UserMessage + "\n" + c.AiResponse, pairs);

            result.Add(matched != null ? c with { WorkspaceHash = matched } : c);
        }

        return result;
    }

    private static string? BestPrefix(string norm, List<(string Hash, string Folder)> pairs)
    {
        foreach (var (hash, folder) in pairs)
            if (norm.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return hash;
        return null;
    }

    private static string? FindWorkspaceInText(
        string text, List<(string Hash, string Folder)> pairs)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // Cap scan to first 4KB — enough to find file refs, avoids huge AI responses
        var scan = text.Length > 4096 ? text[..4096] : text;
        foreach (Match m in WinPathRegex.Matches(scan))
        {
            var norm = NormalizePath(m.Value);
            var hash = BestPrefix(norm, pairs);
            if (hash != null) return hash;
        }
        return null;
    }

    private static string NormalizePath(string p)
    {
        if (p.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            p = Uri.UnescapeDataString(p[8..]).Replace('/', Path.DirectorySeparatorChar);
        return p.TrimEnd('\\', '/');
    }

    private static Dictionary<string, string> BuildWorkspaceMap(IdeTarget target)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (target.WorkspaceStoragePath is null
            || !Directory.Exists(target.WorkspaceStoragePath))
            return map;

        foreach (var wsDir in Directory.EnumerateDirectories(target.WorkspaceStoragePath))
        {
            var hash     = Path.GetFileName(wsDir);
            var jsonPath = Path.Combine(wsDir, "workspace.json");
            if (!File.Exists(jsonPath)) continue;

            try
            {
                using var doc  = JsonDocument.Parse(File.ReadAllText(jsonPath),
                    new JsonDocumentOptions { AllowTrailingCommas = true });
                var root    = doc.RootElement;
                string? folder = null;

                if (root.TryGetProperty("folder", out var f))
                    folder = f.GetString();
                else if (root.TryGetProperty("folders", out var fs)
                    && fs.ValueKind == JsonValueKind.Array
                    && fs.GetArrayLength() > 0)
                    folder = fs[0].TryGetProperty("path", out var p) ? p.GetString() : null;

                if (folder is not null)
                {
                    if (folder.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                        folder = Uri.UnescapeDataString(folder[8..]).Replace('/', '\\');
                    map[hash] = folder;
                }
            }
            catch { }
        }

        return map;
    }
}
