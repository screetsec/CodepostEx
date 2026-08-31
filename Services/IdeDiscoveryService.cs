using CodepostEx.Core;
using CodepostEx.Models;
using Microsoft.Win32;

namespace CodepostEx.Services;

public sealed class IdeDiscoveryService
{
    // Exe paths relative to %LOCALAPPDATA%\Programs\
    private static readonly Dictionary<string, string[]> LocalExePaths = new()
    {
        ["Cursor"]      = [@"cursor\Cursor.exe"],
        ["Windsurf"]    = [@"windsurf\Windsurf.exe", @"Windsurf\Windsurf.exe"],
        ["Kiro"]        = [@"Kiro\Kiro.exe", @"kiro\kiro.exe"],
        ["Trae"]        = [@"Trae\Trae.exe", @"trae\trae.exe"],
        ["Antigravity"] = [@"Antigravity IDE\Antigravity IDE.exe",
                           @"Antigravity\Antigravity.exe",
                           @"antigravity\Antigravity.exe"],
    };

    // Exe paths relative to %ProgramFiles%
    private static readonly Dictionary<string, string[]> PfExePaths = new()
    {
        ["Cursor"]      = [@"Cursor\Cursor.exe"],
        ["Windsurf"]    = [@"Windsurf\Windsurf.exe"],
        ["Kiro"]        = [@"Kiro\Kiro.exe"],
        ["Trae"]        = [@"Trae\Trae.exe"],
        ["Antigravity"] = [@"Antigravity IDE\Antigravity IDE.exe",
                           @"Antigravity\Antigravity.exe"],
    };

    // Registry subkeys to check under HKCU and HKLM\SOFTWARE
    private static readonly Dictionary<string, string[]> RegSubKeys = new()
    {
        ["Cursor"]      = ["Cursor"],
        ["Windsurf"]    = ["Windsurf"],
        ["Kiro"]        = ["Kiro"],
        ["Trae"]        = ["Trae"],
        ["Antigravity"] = ["Antigravity", "Antigravity IDE"],
    };

    public IdeTarget? Resolve(IdeProfile profile, PathResolver paths)
    {
        var exePath = FindExe(profile.Name, paths);

        // Validate AppData path (primary or alternate)
        var appDataPath = ResolveAppData(profile, paths, out var resolvedSubPath);
        if (appDataPath is null) return null; // not installed at all

        // When an alternate AppData folder was matched (e.g. "Antigravity IDE\User" instead of
        // "Antigravity\User"), remap all storage subpaths to use the same base folder so that
        // GlobalStoragePath and WorkspaceStoragePath point to the actual install location.
        var primaryBase  = profile.AppDataSubPath.Split('\\')[0];
        var resolvedBase = resolvedSubPath.Split('\\')[0];
        string Remap(string subPath) =>
            resolvedBase.Equals(primaryBase, StringComparison.OrdinalIgnoreCase)
                ? subPath
                : subPath.StartsWith(primaryBase + @"\", StringComparison.OrdinalIgnoreCase)
                    ? resolvedBase + subPath[primaryBase.Length..]
                    : subPath;

        var globalStorage = paths.Resolve(Remap(profile.GlobalStorageSubPath));
        var localState    = paths.Resolve(Remap(profile.LocalStateSubPath));
        var wsStorage     = profile.WorkspaceStorageSubPath is null
            ? null
            : paths.Resolve(Remap(profile.WorkspaceStorageSubPath));

        return new IdeTarget(profile, exePath, appDataPath, globalStorage, localState, wsStorage);
    }

    public IEnumerable<IdeTarget> ResolveAll(IEnumerable<IdeProfile> profiles, PathResolver paths)
    {
        foreach (var p in profiles)
        {
            var target = Resolve(p, paths);
            if (target is not null) yield return target;
        }
    }

    private static string? ResolveAppData(IdeProfile profile, PathResolver paths, out string resolvedSubPath)
    {
        resolvedSubPath = profile.AppDataSubPath;

        // Primary AppData path
        var primary = paths.Resolve(profile.AppDataSubPath);
        if (Directory.Exists(primary) || File.Exists(paths.Resolve(profile.GlobalStorageSubPath)))
            return primary;

        // Alternate AppData paths (e.g. "Antigravity IDE\User", "trae\User")
        foreach (var alt in profile.AlternateAppDataSubPaths)
        {
            var altPath = paths.Resolve(alt);
            if (Directory.Exists(altPath))
            {
                resolvedSubPath = alt;
                return altPath;
            }
        }

        // Registry existence means installed even if AppData missing
        if (HasRegistryKey(profile.Name)) return primary;

        return null;
    }

    private static string? FindExe(string name, PathResolver paths)
    {
        // LocalAppData\Programs
        if (LocalExePaths.TryGetValue(name, out var localPaths))
        {
            foreach (var rel in localPaths)
            {
                var full = Path.Combine(paths.LocalAppData, "Programs", rel);
                if (File.Exists(full)) return full;
            }
        }

        // ProgramFiles
        var pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (PfExePaths.TryGetValue(name, out var pfPaths))
        {
            foreach (var rel in pfPaths)
            {
                foreach (var root in new[] { pf, pfx })
                {
                    var full = Path.Combine(root, rel);
                    if (File.Exists(full)) return full;
                }
            }
        }

        return null;
    }

    private static bool HasRegistryKey(string name)
    {
        if (!RegSubKeys.TryGetValue(name, out var subKeys)) return false;

        foreach (var sub in subKeys)
        {
            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(@$"SOFTWARE\{sub}");
                if (hkcu != null) return true;
                using var hklm = Registry.LocalMachine.OpenSubKey(@$"SOFTWARE\{sub}");
                if (hklm != null) return true;
            }
            catch { }
        }

        return false;
    }
}
