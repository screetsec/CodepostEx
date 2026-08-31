using System.Text.Json;
using CodepostEx.Core;
using CodepostEx.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CodepostEx.Modules;

public sealed class UserModule
{
    public static void Run(string ide, IServiceProvider sp)
    {
        var paths = sp.GetRequiredService<PathResolver>();

        foreach (var profile in IdeProfiles.Filter(ide))
        {
            Out.Blank();
            Out.Star($"[{profile.Name}] Account info:");

            if (profile.Name.Equals("Cursor", StringComparison.OrdinalIgnoreCase))
                ReadCursorUser(profile, paths);
            else
                ReadGenericUser(profile, paths);
        }
    }

    private static void ReadCursorUser(IdeProfile profile, PathResolver paths)
    {
        if (profile.SentrySubPath is null)
        {
            Out.Minus("SentrySubPath not defined for Cursor profile.");
            return;
        }

        var scopePath = paths.Resolve(profile.SentrySubPath);
        if (!File.Exists(scopePath))
        {
            Out.Minus($"Scope file not found: {scopePath}");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(scopePath),
                new JsonDocumentOptions { AllowTrailingCommas = true });

            var root = doc.RootElement;

            Out.Plus("Cursor auth state:");

            if (root.TryGetProperty("scope", out var scope)
                && scope.TryGetProperty("user", out var user))
            {
                PrintField("LoggedIn",  "true");
                PrintField("AccountId", GetStr(user, "id"));
                PrintField("Email",     GetStr(user, "email"));
                PrintField("Plan",      GetStr(user, "plan"));
            }
            else
            {
                PrintField("LoggedIn", "false");
            }

            if (root.TryGetProperty("event", out var ev)
                && ev.TryGetProperty("contexts", out var ctx))
            {
                if (ctx.TryGetProperty("app", out var app))
                {
                    Out.Blank();
                    Out.Plus("Application:");
                    PrintField("AppName",    GetStr(app, "app_name"));
                    PrintField("AppVersion", GetStr(app, "app_version"));
                    PrintField("Arch",       GetStr(app, "app_arch"));
                }

                if (ctx.TryGetProperty("device", out var dev))
                {
                    Out.Blank();
                    Out.Plus("Device:");
                    PrintField("CPU",        GetStr(dev, "cpu_description"));
                    PrintField("Processors", GetStr(dev, "processor_count"));
                    if (dev.TryGetProperty("memory_size", out var memEl)
                        && memEl.TryGetInt64(out var mem))
                        PrintField("Memory", $"{Math.Round(mem / (double)(1024 * 1024 * 1024), 2)} GB");
                    PrintField("Screen", GetStr(dev, "screen_resolution"));
                }

                if (ctx.TryGetProperty("os", out var os))
                {
                    Out.Blank();
                    Out.Plus("OS:");
                    PrintField("Name",    GetStr(os, "name"));
                    PrintField("Version", GetStr(os, "version"));
                }
            }
        }
        catch (Exception ex)
        {
            Out.Minus($"Failed to parse scope file: {ex.Message}");
        }
    }

    private static void ReadGenericUser(IdeProfile profile, PathResolver paths)
    {
        var localStatePath = paths.Resolve(profile.LocalStateSubPath);
        if (!File.Exists(localStatePath))
        {
            Out.Minus($"User info not available for {profile.Name}. Use -dump-token -decode-token to read auth tokens.");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(localStatePath),
                new JsonDocumentOptions { AllowTrailingCommas = true });

            var root  = doc.RootElement;
            bool found = false;

            // Chromium-style profile.info_cache
            if (root.TryGetProperty("profile", out var profEl)
                && profEl.TryGetProperty("info_cache", out var infoCache))
            {
                foreach (var entry in infoCache.EnumerateObject())
                {
                    var name  = GetStr(entry.Value, "name") ?? GetStr(entry.Value, "user_name");
                    var email = GetStr(entry.Value, "email");
                    if (name is not null || email is not null)
                    {
                        found = true;
                        if (name  is not null) PrintField("Name",  name);
                        if (email is not null) PrintField("Email", email);
                        break;
                    }
                }
            }

            // Generic top-level account fields
            if (!found)
            {
                foreach (var field in new[] { "account_id", "user_id", "email", "username", "name", "displayName" })
                {
                    var val = GetStr(root, field);
                    if (val is not null) { PrintField(field, val); found = true; }
                }
            }

            if (!found)
                Out.Minus($"No account info found in local state for {profile.Name}. Use -dump-token -decode-token to read auth tokens.");
        }
        catch (Exception ex)
        {
            Out.Minus($"Failed to parse local state for {profile.Name}: {ex.Message}");
        }
    }

    private static string? GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

    private static void PrintField(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Out.Item($"{label}: {value}");
    }
}
