using System.Text.Json;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class TokensModule
{
    public static void Run(string ide, bool decode, bool validate, string output, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var extractor = sp.GetRequiredService<TokenExtractor>();
        var paths     = sp.GetRequiredService<PathResolver>();

        var profiles = IdeProfiles.Filter(ide)
                                  .Where(p => p.TokenKeys.Length > 0)
                                  .ToList();

        if (profiles.Count == 0)
        {
            Out.Minus($"No token keys defined for --ide {ide}");
            return;
        }

        var targets = discovery.ResolveAll(profiles, paths).ToList();
        if (targets.Count == 0)
        {
            Out.Minus($"No IDE detected for --ide {ide}");
            return;
        }

        var allTokens = new List<TokenResult>();

        foreach (var target in targets)
        {
            Out.Star($"Extracting tokens from {target.Name}...");
            var tokens = extractor.Extract(target);

            if (tokens.Count == 0)
            {
                Out.Minus($"No tokens found in {target.Name}");
                continue;
            }

            foreach (var t in tokens)
            {
                var entry = (decode || validate) ? extractor.Analyze(t, decode, validate) : t;
                allTokens.Add(entry);

                Out.Plus($"{entry.TokenType}");
                Out.Item($"Source: {entry.SourceKey}");
                Out.Item($"DB: {entry.Database}");
                var preview = entry.Value.Length > 120 ? entry.Value[..120] + "..." : entry.Value;
                Out.Item($"Value: {preview}");

                if (entry.Analysis is { } a)
                {
                    if (a.Subject is not null)           Out.Item($"Subject: {a.Subject}");
                    if (a.Issuer is not null)            Out.Item($"Issuer: {a.Issuer}");
                    if (a.ExpiresAt.HasValue)            Out.Item($"Expires: {a.ExpiresAt}");
                    if (a.IsExpired.HasValue)            Out.Item($"Expired: {a.IsExpired}");
                    Out.Item($"Status: {a.ValidationStatus}");
                    if (a.NetworkValidation is not null) Out.Item($"Online: {a.NetworkValidation}");
                    if (a.TokenFormat is not ("Jwt" or null)) Out.Item($"Format: {a.TokenFormat}");
                }
                Out.Blank();
            }
        }

        if (allTokens.Count == 0) return;

        Out.Plus($"Total tokens: {allTokens.Count}");

        try
        {
            Directory.CreateDirectory(output);
            var stamp   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outFile = Path.Combine(output, $"tokens_{stamp}.json");
            var json    = JsonSerializer.Serialize(allTokens, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outFile, json, System.Text.Encoding.UTF8);
            Out.Plus($"Saved: {outFile}");
        }
        catch (Exception ex)
        {
            Out.Minus($"Failed to write output: {ex.Message}");
        }
    }
}
