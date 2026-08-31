using System.IO.Compression;
using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Output;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class ArtifactsModule
{
    public static void Run(string ide, string output, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var paths     = sp.GetRequiredService<PathResolver>();

        var artifactsDir = Path.Combine(output, "Artifacts");
        Directory.CreateDirectory(artifactsDir);

        foreach (var profile in IdeProfiles.Filter(ide))
        {
            var target = discovery.Resolve(profile, paths);
            if (target is null) { Out.Minus($"{profile.Name}: not detected."); continue; }

            var zipPath = OutputPaths.HistoryZip(output, target.Name);

            var existing = Directory.GetFiles(artifactsDir,
                $"{target.Name}_History_*.zip", SearchOption.TopDirectoryOnly);
            if (existing.Length > 0)
            {
                Out.Plus($"{target.Name}: already collected — {existing[0]}");
                continue;
            }

            Out.Star($"Collecting {target.Name} artifacts...");

            if (profile.HistorySubPath is null)
                continue;
            var histPath     = paths.Resolve(profile.HistorySubPath);
            var settingsPath = paths.Resolve(profile.SettingsSubPath);

            if (!Directory.Exists(histPath))
            {
                Out.Minus($"History path not found: {histPath}");
                continue;
            }

            // Stream directly into the ZIP — eliminates the copy-to-temp-dir round-trip.
            int collected = 0;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                foreach (var file in Directory.EnumerateFiles(histPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        zip.CreateEntryFromFile(file, Path.GetRelativePath(histPath, file), CompressionLevel.Optimal);
                        collected++;
                    }
                    catch { }
                }

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        zip.CreateEntryFromFile(settingsPath, Path.GetFileName(settingsPath), CompressionLevel.Optimal);
                        collected++;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Out.Minus($"{target.Name}: collect failed — {ex.Message}");
                try { File.Delete(zipPath); } catch { }
                continue;
            }

            if (collected == 0)
            {
                Out.Minus($"{target.Name}: nothing to collect.");
                try { File.Delete(zipPath); } catch { }
                continue;
            }

            Out.Plus($"{target.Name}: {collected} file(s) → {zipPath}");
        }
    }
}
