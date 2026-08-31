using CodepostEx.Core;
using CodepostEx.Models;
using CodepostEx.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class DiscoverModule
{
    public static void Run(string ide, IServiceProvider sp)
    {
        var discovery = sp.GetRequiredService<IdeDiscoveryService>();
        var paths     = sp.GetRequiredService<PathResolver>();

        bool found = false;
        foreach (var profile in IdeProfiles.Filter(ide))
        {
            var target = discovery.Resolve(profile, paths);
            if (target is null)
            {
                Out.Minus($"{profile.Name}: not detected");
            }
            else
            {
                found = true;
                Out.Plus($"{target.Name}");
                Out.Item("Status: Available");
                Out.Item($"Exe: {target.ExecutablePath ?? "(not found in PATH/Programs)"}");
                Out.Item($"AppData: {target.AppDataPath}");
                Out.Item($"Storage: {target.Profile.StorageType}");
                var dbExists = File.Exists(target.GlobalStoragePath);
                Out.Item($"DB: {(dbExists ? target.GlobalStoragePath : "(missing)")}");
                Out.Blank();
            }
        }

        if (!found)
            Out.Minus("No AI IDEs detected.");
    }
}
