using System.Text.Json;
using System.Text.RegularExpressions;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class TrustModule
{
    private static readonly Regex TrustPattern = new(
        @"""fsPath""\s*:\s*""((?:\\.|[^""\\])*)""\s*,[\s\S]*?""trusted""\s*:\s*(true|false)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Run(string ide, bool includeMetadata, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var paths     = sp.GetRequiredService<PathResolver>();
        var vscdb     = sp.GetRequiredService<VscdbService>();

        foreach (var profile in IdeProfiles.Filter(ide))
        {
            var target = discovery.Resolve(profile, paths);
            if (target is null) { Out.Minus($"{profile.Name}: not detected."); continue; }

            var entries = new Dictionary<string, TrustEntry>(StringComparer.OrdinalIgnoreCase);

            var dbPaths = new List<string> { target.GlobalStoragePath };
            foreach (var alt in profile.AlternateAppDataSubPaths)
                dbPaths.Add(Path.GetFullPath(paths.Resolve(Path.Combine(alt, "..", "globalStorage", "state.vscdb"))));

            foreach (var dbPath in dbPaths.Where(File.Exists).Distinct())
                ReadTrustFromDb(dbPath, vscdb, entries);

            if (includeMetadata && target.WorkspaceStoragePath is not null
                && Directory.Exists(target.WorkspaceStoragePath))
                ReadWorkspaceStoragePaths(target.WorkspaceStoragePath, entries);

            var trustedList   = entries.Values.Where(e =>  e.Trusted).OrderBy(e => e.FsPath).ToList();
            var untrustedList = entries.Values.Where(e => !e.Trusted).OrderBy(e => e.FsPath).ToList();

            if (trustedList.Count == 0)
            {
                Out.Minus($"{target.Name}: no trusted workspaces found.");
                continue;
            }

            Out.Plus($"{target.Name} - {trustedList.Count} trusted workspace(s)");
            foreach (var e in trustedList)
                Out.Item(e.FsPath);

            if (includeMetadata && untrustedList.Count > 0)
            {
                Out.Blank();
                Out.Plus($"Workspace storage paths (not in trust DB): {untrustedList.Count}");
                foreach (var e in untrustedList)
                    Out.Item(e.FsPath);
            }
        }
    }

    private static void ReadTrustFromDb(
        string dbPath, VscdbService vscdb,
        Dictionary<string, TrustEntry> entries)
    {
        using var conn = vscdb.OpenReadOnly(dbPath);
        if (conn is null) return;

        var value = VscdbService.GetValue(conn, "content.trust.model.key");
        if (string.IsNullOrWhiteSpace(value)) return;

        foreach (Match m in TrustPattern.Matches(value))
        {
            var fsPath  = m.Groups[1].Value.Replace("\\/", "/").Replace("\\\\", "\\");
            var isTrusted = m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (!IsValidPath(fsPath)) continue;

            // Normalize drive letter to uppercase (C:\ not c:\)
            if (fsPath.Length >= 2 && char.IsLetter(fsPath[0]) && fsPath[1] == ':')
                fsPath = char.ToUpperInvariant(fsPath[0]) + fsPath[1..];

            var key = fsPath.TrimEnd('\\').ToLowerInvariant();
            if (!entries.ContainsKey(key))
                entries[key] = new TrustEntry(fsPath.TrimEnd('\\'), isTrusted, dbPath);
        }
    }

    private static void ReadWorkspaceStoragePaths(
        string wsStoragePath,
        Dictionary<string, TrustEntry> entries)
    {
        foreach (var wsDir in Directory.EnumerateDirectories(wsStoragePath))
        {
            var jsonPath = Path.Combine(wsDir, "workspace.json");
            if (!File.Exists(jsonPath)) continue;

            try
            {
                using var doc  = JsonDocument.Parse(File.ReadAllText(jsonPath),
                    new JsonDocumentOptions { AllowTrailingCommas = true });
                var root    = doc.RootElement;
                string? folder = null;

                if (root.TryGetProperty("folder", out var f)) folder = f.GetString();
                else if (root.TryGetProperty("folders", out var fs)
                    && fs.GetArrayLength() > 0
                    && fs[0].TryGetProperty("path", out var p))
                    folder = p.GetString();

                if (folder is null) continue;

                if (folder.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    folder = Uri.UnescapeDataString(folder[8..]).Replace('/', '\\');

                // Normalize drive letter to uppercase
                if (folder.Length >= 2 && char.IsLetter(folder[0]) && folder[1] == ':')
                    folder = char.ToUpperInvariant(folder[0]) + folder[1..];

                var key = folder.TrimEnd('\\').ToLowerInvariant();
                if (!entries.ContainsKey(key))
                    entries[key] = new TrustEntry(folder.TrimEnd('\\'), false, jsonPath);
            }
            catch { }
        }
    }

    private static bool IsValidPath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && (Regex.IsMatch(path, @"^[a-zA-Z]:\\") || path.StartsWith('/'));
}
