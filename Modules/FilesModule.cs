using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class FilesModule
{
    private static readonly Dictionary<string, string> ExtMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".env"]        = "Environment Config",
        [".yml"]        = "Configuration",
        [".yaml"]       = "Configuration",
        [".xml"]        = "Configuration",
        [".conf"]       = "Configuration",
        [".config"]     = "Configuration",
        [".properties"] = "Configuration",
        [".ini"]        = "Configuration",
        [".toml"]       = "Configuration",
        [".key"]        = "Key File",
        [".pem"]        = "Certificate",
        [".p12"]        = "Certificate",
        [".pfx"]        = "Certificate",
    };

    private static readonly Dictionary<string, string> ExactMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["package.json"]           = "Node.js Config",
        ["pom.xml"]                = "Maven Config",
        ["build.gradle"]           = "Gradle Config",
        ["build.gradle.kts"]       = "Gradle Config",
        ["gemfile"]                = "Ruby Config",
        ["gemfile.lock"]           = "Ruby Config",
        ["requirements.txt"]       = "Python Dependencies",
        ["composer.json"]          = "PHP Dependencies",
        ["composer.lock"]          = "PHP Dependencies",
        ["dockerfile"]             = "Docker Config",
        ["docker-compose.yml"]     = "Docker Compose",
        ["docker-compose.yaml"]    = "Docker Compose",
        ["application.properties"] = "Java Config",
        ["application.yml"]        = "Java Config",
        ["application.yaml"]       = "Java Config",
        ["settings.py"]            = "Python Config",
        ["config.py"]              = "Python Config",
        ["settings.json"]          = "Settings",
        ["config.json"]            = "Configuration",
    };

    private static readonly (string Sub, string Label)[] NameSubs =
    [
        ("secret",     "Secret"),
        ("password",   "Password"),
        ("credential", "Credential"),
        ("token",      "Token"),
        ("config",     "Configuration"),
        ("settings",   "Settings"),
        ("key",        "Key"),
        ("backup",     "Backup"),
    ];

    public static void Run(string ide, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var paths     = sp.GetRequiredService<PathResolver>();

        foreach (var profile in IdeProfiles.Filter(ide))
        {
            var target = discovery.Resolve(profile, paths);
            if (target is null) { Out.Minus($"{profile.Name}: not detected."); continue; }

            if (profile.HistorySubPath is null)
            {
                Out.Minus($"History not available for {profile.Name}.");
                continue;
            }
            var histPath = paths.Resolve(profile.HistorySubPath);
            Out.Star($"Scanning {target.Name} history: {histPath}");

            if (!Directory.Exists(histPath))
            {
                Out.Minus($"History path not found: {histPath}");
                continue;
            }

            var found = 0;
            foreach (var file in Directory.EnumerateFiles(histPath, "*", SearchOption.AllDirectories))
            {
                var label = Classify(file);
                if (label is null) continue;

                found++;
                var rel = Path.GetRelativePath(histPath, file);
                Out.Plus(label);
                Out.Item($"File: {Path.GetFileName(file)}");
                Out.Item($"Path: {rel}");
                Out.Blank();
            }

            if (found == 0) Out.Minus($"{target.Name}: no interesting files found.");
            else            Out.Plus($"{target.Name}: {found} interesting file(s) found.");
        }
    }

    private static string? Classify(string filePath)
    {
        var name  = Path.GetFileName(filePath);
        var lname = name.ToLowerInvariant();
        var ext   = Path.GetExtension(lname);

        if (ExactMap.TryGetValue(lname, out var label)) return label;
        if (lname.StartsWith(".env")) return "Environment Config";
        if (ExtMap.TryGetValue(ext, out var extLabel)) return extLabel;

        foreach (var (sub, subLabel) in NameSubs)
            if (lname.Contains(sub)) return subLabel;

        return null;
    }
}
