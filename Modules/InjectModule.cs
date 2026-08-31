using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class InjectModule
{
    private static readonly Regex TrustPattern = new(
        @"""fsPath""\s*:\s*""((?:\\.|[^""\\])*)""\s*,[\s\S]*?""trusted""\s*:\s*(true|false)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Run(
        string ide, string workspace, string payloadName, bool force, IServiceProvider sp)
    {
        if (!payloadName.Equals("tasks.json", StringComparison.OrdinalIgnoreCase))
        {
            Out.Minus("Only tasks.json payload injection is currently supported");
            return;
        }

        var resolvedWs = Path.GetFullPath(workspace);
        if (!Directory.Exists(resolvedWs))
        {
            Out.Minus($"Workspace path not found: {resolvedWs}");
            return;
        }

        Out.Star($"Injecting '{payloadName}' into: {resolvedWs}");

        // -- Trust check ------------------------------------------------
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var vscdb     = sp.GetRequiredService<VscdbService>();
        var paths     = sp.GetRequiredService<PathResolver>();

        bool trustFound = false, trusted = false;
        string trustSource = "none";

        foreach (var profile in IdeProfiles.Filter(ide))
        {
            var target = discovery.Resolve(profile, paths);
            if (target is null) continue;

            if (!File.Exists(target.GlobalStoragePath)) continue;
            using var conn = vscdb.OpenReadOnly(target.GlobalStoragePath);
            if (conn is null) continue;

            var value = VscdbService.GetValue(conn, "content.trust.model.key");
            if (string.IsNullOrWhiteSpace(value)) continue;

            var lookupKey = resolvedWs.TrimEnd('\\').ToLowerInvariant();

            foreach (Match m in TrustPattern.Matches(value))
            {
                var fsPath = m.Groups[1].Value.Replace("\\/", "/").Replace("\\\\", "\\");
                var key    = fsPath.TrimEnd('\\').ToLowerInvariant();
                if (!key.Equals(lookupKey, StringComparison.OrdinalIgnoreCase)) continue;

                trustFound  = true;
                trusted     = m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                trustSource = $"{profile.Name}:{target.GlobalStoragePath}";
                break;
            }

            if (trustFound) break;
        }

        if (trustFound && trusted)
            Out.Plus($"Workspace explicitly trusted (source: {trustSource})");
        else if (trustFound)
            Out.Warn($"Workspace trust=false in IDE DB (source: {trustSource}); payload may not auto-run");
        else
            Out.Warn("Workspace not in IDE trust inventory; payload may not auto-run until folder is trusted");

        // -- Load payload -----------------------------------------------
        string payloadJson;
        try
        {
            payloadJson = AssetLoader.Load($"payloads/{payloadName}");
        }
        catch
        {
            Out.Minus($"Embedded asset not found: payloads/{payloadName}");
            return;
        }

        try { JsonDocument.Parse(payloadJson); }
        catch (Exception ex) { Out.Minus($"Payload is not valid JSON: {ex.Message}"); return; }

        // -- Write ------------------------------------------------------
        var vscodeDir  = Path.Combine(resolvedWs, ".vscode");
        var targetFile = Path.Combine(vscodeDir, payloadName);

        if (File.Exists(targetFile) && !force)
        {
            Out.Minus($"Payload already exists: {targetFile}");
            Out.Minus("Use --force to merge into existing tasks.json");
            return;
        }

        if (!Directory.Exists(vscodeDir))
        {
            Directory.CreateDirectory(vscodeDir);
            Out.Plus($"Created directory: {vscodeDir}");
        }

        if (File.Exists(targetFile) && force)
        {
            try
            {
                var existing = JsonNode.Parse(File.ReadAllText(targetFile, System.Text.Encoding.UTF8));
                var incoming = JsonNode.Parse(payloadJson);

                var existingTasks = existing?["tasks"]?.AsArray();
                var incomingTasks = incoming?["tasks"]?.AsArray();

                if (existingTasks is not null && incomingTasks is not null)
                {
                    foreach (var task in incomingTasks)
                        existingTasks.Add(task?.DeepClone());

                    payloadJson = existing!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    Out.Plus("Merged payload tasks into existing tasks.json");
                }
            }
            catch (Exception ex)
            {
                Out.Warn($"Merge failed ({ex.Message}); writing fresh payload instead");
            }
        }

        File.WriteAllText(targetFile, payloadJson);
        Out.Plus($"Injected payload: {targetFile}");
        Out.Plus("Persistence trigger: runOn folderOpen (re-executes each time workspace is opened)");
    }
}
