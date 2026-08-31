using System.Text.Json;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class SearchModule
{
    private static readonly HashSet<string> KnownCategories =
        new(StringComparer.OrdinalIgnoreCase) { "All", "Credentials", "Emails" };

    public static void Run(string ide, string[] search, string output, IServiceProvider sp)
    {
        bool doAll         = search.Any(s => s.Equals("All", StringComparison.OrdinalIgnoreCase));
        bool doCredentials = doAll || search.Any(s => s.Contains("Credential", StringComparison.OrdinalIgnoreCase));
        bool doEmails      = doAll || search.Any(s => s.Contains("Email",      StringComparison.OrdinalIgnoreCase));

        // Terms that don't match any known category are treated as free-text
        var freeTextTerms = search
            .Where(s => !KnownCategories.Contains(s)
                     && !s.Contains("Credential", StringComparison.OrdinalIgnoreCase)
                     && !s.Contains("Email",      StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var scanner   = sp.GetRequiredService<CredentialScanner>();
        var paths     = sp.GetRequiredService<PathResolver>();

        var profiles = IdeProfiles.Filter(ide).ToList();
        var targets  = discovery.ResolveAll(profiles, paths).ToList();

        if (targets.Count == 0)
        {
            Out.Minus($"No IDE detected for -ide {ide}");
            return;
        }

        var allFindings = new List<CredentialFinding>();

        foreach (var target in targets)
        {
            if (target.Profile.HistorySubPath is null)
            {
                Out.Minus($"History not available for {target.Name}.");
                continue;
            }
            var historyPath = paths.Resolve(target.Profile.HistorySubPath);
            Out.Star($"Scanning {target.Name} history: {historyPath}");

            if (!Directory.Exists(historyPath))
            {
                Out.Minus($"History path not found: {historyPath}");
                continue;
            }

            // Predefined categories
            if (doCredentials || doEmails)
            {
                var findings = scanner.ScanDirectory(historyPath, doCredentials, doEmails);
                foreach (var f in findings)
                {
                    Out.Plus($"[{f.Category}] {f.Type}: {f.Value}");
                    Out.Item($"File: {f.FileName}");
                    Out.Blank();
                    allFindings.Add(f);
                }
                if (findings.Count == 0 && freeTextTerms.Length == 0)
                    Out.Minus($"No matches in {target.Name}");
            }

            // Free-text searches
            foreach (var term in freeTextTerms)
            {
                Out.Star($"Free-text search \"{term}\" in {target.Name}...");
                var findings = scanner.ScanDirectoryFreeText(historyPath, term);
                if (findings.Count == 0)
                {
                    Out.Minus($"No matches for \"{term}\" in {target.Name}");
                    continue;
                }
                foreach (var f in findings)
                {
                    Out.Plus($"[FreeText] \"{f.Type}\": ...{f.Value}...");
                    Out.Item($"File: {f.FileName}");
                    Out.Blank();
                    allFindings.Add(f);
                }
            }
        }

        if (allFindings.Count == 0)
        {
            Out.Minus("No matches found");
            return;
        }

        Out.Plus($"Total findings: {allFindings.Count}");

        try
        {
            Directory.CreateDirectory(output);
            var stamp   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outFile = Path.Combine(output, $"search_{stamp}.json");
            var json    = JsonSerializer.Serialize(allFindings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outFile, json, System.Text.Encoding.UTF8);
            Out.Plus($"Saved: {outFile}");
        }
        catch (Exception ex)
        {
            Out.Minus($"Failed to write output: {ex.Message}");
        }
    }
}
