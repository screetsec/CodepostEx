using System.IO.Compression;
using System.Text.Json;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class HistoryModule
{
    private static readonly Dictionary<string, string> ExtCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        [".key"]        = "Private Key",
        [".pem"]        = "Certificate",
        [".p12"]        = "Certificate",
        [".pfx"]        = "Certificate",
        [".yml"]        = "YAML Config",
        [".yaml"]       = "YAML Config",
        [".xml"]        = "XML Config",
        [".conf"]       = "Config File",
        [".config"]     = "Config File",
        [".properties"] = "Config File",
        [".ini"]        = "Config File",
        [".toml"]       = "Config File",
    };

    private static readonly Dictionary<string, string> ExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["package.json"]           = "Node.js Config",
        ["pom.xml"]                = "Maven Config",
        ["build.gradle"]           = "Gradle Config",
        ["build.gradle.kts"]       = "Gradle Config",
        ["gemfile"]                = "Ruby Config",
        ["gemfile.lock"]           = "Ruby Config",
        ["requirements.txt"]       = "Python Deps",
        ["composer.json"]          = "PHP Deps",
        ["composer.lock"]          = "PHP Deps",
        ["dockerfile"]             = "Docker Config",
        ["docker-compose.yml"]     = "Docker Compose",
        ["docker-compose.yaml"]    = "Docker Compose",
        ["application.properties"] = "Java Config",
        ["application.yml"]        = "Java Config",
        ["application.yaml"]       = "Java Config",
        ["settings.py"]            = "Python Config",
        ["config.py"]              = "Python Config",
        ["settings.json"]          = "IDE Settings",
        ["config.json"]            = "Config File",
        [".gitconfig"]             = "Git Config",
        [".npmrc"]                 = "NPM Config",
        [".pypirc"]                = "PyPI Credentials",
        [".netrc"]                 = "Network Credentials",
        [".aws/credentials"]       = "AWS Credentials",
        ["credentials"]            = "Credentials",
    };

    private static readonly (string Keyword, string Category)[] Keywords =
    [
        ("password",   "Password File"),
        ("passwd",     "Password File"),
        ("credential", "Credentials"),
        ("secret",     "Secret File"),
        ("apikey",     "API Key"),
        ("api_key",    "API Key"),
        ("token",      "Token File"),
        ("private",    "Private Key"),
        ("backup",     "Backup File"),
        ("config",     "Config File"),
    ];

    public static void Run(
        string ide, string output, bool interesting, bool noZip, bool force, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var paths     = sp.GetRequiredService<PathResolver>();

        var targets = IdeProfiles.Filter(ide)
            .Select(p => discovery.Resolve(p, paths))
            .Where(t => t is not null)
            .Cast<IdeTarget>()
            .ToList();

        if (targets.Count == 0)
        {
            Out.Minus($"No IDE detected for --ide {ide}");
            return;
        }

        if (!noZip)
        {
            try { Directory.CreateDirectory(output); }
            catch (Exception ex) { Out.Minus($"Cannot create output dir: {ex.Message}"); return; }
        }

        int totalZips = 0, totalInteresting = 0;

        foreach (var target in targets)
        {
            if (target.Profile.HistorySubPath is null)
            {
                Out.Minus($"History not available for {target.Name}.");
                continue;
            }
            var historyPath  = paths.Resolve(target.Profile.HistorySubPath);
            var settingsPath = paths.Resolve(target.Profile.SettingsSubPath);
            var toolTag      = target.Profile.Name.Replace(" ", "_");

            if (!Directory.Exists(historyPath))
            {
                Out.Minus($"History not found: {target.Name} ({historyPath})");
                continue;
            }

            Out.Star($"Processing {target.Name}: {historyPath}");

            if (interesting)
                totalInteresting += ScanInteresting(historyPath, target.Name);

            if (!noZip)
            {
                if (!force)
                {
                    var existing = Directory.EnumerateFiles(output, $"{toolTag}_History_*.zip")
                        .FirstOrDefault();
                    if (existing is not null)
                    {
                        Out.Plus($"Already collected (--force to re-collect): {existing}");
                        totalZips++;
                        continue;
                    }
                }

                var stamp   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var zipPath = Path.Combine(output, $"{toolTag}_History_{stamp}.zip");
                var count   = CreateZip(historyPath, settingsPath, zipPath);

                if (count > 0)
                {
                    Out.Plus($"Zipped {count} file(s): {zipPath}");
                    totalZips++;
                }
                else
                {
                    Out.Minus($"No files collected for {target.Name}");
                    try { File.Delete(zipPath); } catch { }
                }
            }
        }

        Out.Plus($"Done — {totalZips} zip(s) created, {totalInteresting} interesting original file(s) found");
    }

    private static int CreateZip(string historyPath, string settingsPath, string zipPath)
    {
        int count = 0;
        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            foreach (var file in Directory.EnumerateFiles(historyPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(historyPath, file), CompressionLevel.Fastest);
                    count++;
                }
                catch { }
            }

            if (File.Exists(settingsPath))
            {
                try { zip.CreateEntryFromFile(settingsPath, "settings.json", CompressionLevel.Fastest); count++; }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Out.Minus($"Zip failed: {ex.Message}");
            return 0;
        }
        return count;
    }

    private static int ScanInteresting(string historyPath, string toolName)
    {
        int found = 0;
        Out.Star($"Scanning {toolName} history entries for sensitive originals...");

        foreach (var entriesFile in Directory.EnumerateFiles(historyPath, "entries.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(entriesFile, System.Text.Encoding.UTF8));
                var root = doc.RootElement;

                if (!root.TryGetProperty("resource", out var res)) continue;
                var uri = res.GetString();
                if (string.IsNullOrWhiteSpace(uri)) continue;

                var originalPath   = UriToPath(uri);
                var fileName       = Path.GetFileName(originalPath);
                var classification = Classify(fileName);
                if (classification is null) continue;

                var snapshots = root.TryGetProperty("entries", out var entries)
                    ? entries.GetArrayLength() : 0;

                found++;
                Out.Plus($"[{classification}] {fileName}");
                Out.Item($"Original: {originalPath}");
                Out.Item($"Snapshots: {snapshots}");
                Out.Blank();
            }
            catch { }
        }

        if (found == 0) Out.Minus($"No sensitive originals in {toolName} history");
        return found;
    }

    private static string? Classify(string fileName)
    {
        if (ExactNames.TryGetValue(fileName, out var label)) return label;
        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) return "Env Config";

        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && ExtCategories.TryGetValue(ext, out var extLabel))
            return extLabel;

        var bare = Path.GetFileNameWithoutExtension(fileName);
        foreach (var (kw, cat) in Keywords)
            if (bare.Contains(kw, StringComparison.OrdinalIgnoreCase)) return cat;

        return null;
    }

    private static string UriToPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.IsFile)
            return u.LocalPath;
        var raw = uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ? uri[8..] : uri;
        return Uri.UnescapeDataString(raw).Replace('/', Path.DirectorySeparatorChar);
    }
}
