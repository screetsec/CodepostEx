using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodepostEx.Core;
using CodepostEx.Models;
using Microsoft.Extensions.Logging;

namespace CodepostEx.Services;

public sealed class SecretExtractor
{
    private readonly VscdbService _vscdb;
    private readonly AppCache     _cache;
    private readonly ILogger<SecretExtractor> _log;

    public SecretExtractor(VscdbService vscdb, AppCache cache, ILogger<SecretExtractor> log)
    {
        _vscdb = vscdb;
        _cache = cache;
        _log   = log;
    }

    public IReadOnlyList<SecretEntry> Extract(IdeTarget target, bool includeGit = false)
    {
        var dbPath = target.GlobalStoragePath;
        if (!File.Exists(dbPath)) return [];

        using var conn = _vscdb.OpenReadOnly(dbPath);
        if (conn is null) return [];

        var encKey = LoadEncryptionKey(target.LocalStatePath);

        var results = new List<SecretEntry>();
        var seen    = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, value) in VscdbService.GetByKeyPattern(conn, "secret://%"))
        {
            if (!includeGit && IsGitIpcSecret(key)) continue;
            if (!seen.Add(key)) continue;

            var (extId, secretKey) = ParseSecretKey(key);
            if (extId is null) continue;

            var blob = ParseBufferJson(value);
            if (blob is null || blob.Length == 0) continue;

            string? plaintext = null;
            bool decrypted    = false;

            if (encKey is not null)
            {
                plaintext = DecryptV10(encKey, blob);
                decrypted = plaintext is not null;
            }

            results.Add(new SecretEntry(
                Tool:        target.Name,
                ExtensionId: extId,
                SecretKey:   secretKey ?? string.Empty,
                Value:       plaintext,
                Decrypted:   decrypted,
                SourceKey:   key,
                Database:    dbPath));
        }

        results.Sort((a, b) =>
        {
            var c = string.Compare(a.ExtensionId, b.ExtensionId, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.SecretKey, b.SecretKey, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    // ── Key material ──────────────────────────────────────────────────────────

    private byte[]? LoadEncryptionKey(string localStatePath)
    {
        if (_cache.EncryptionKey.TryGetValue(localStatePath, out var cached))
            return cached;

        if (!File.Exists(localStatePath)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt)) return null;
            if (!osCrypt.TryGetProperty("encrypted_key", out var encKeyB64)) return null;

            var encKeyStr = encKeyB64.GetString();
            if (encKeyStr is null) return null;
            var encKeyBytes = Convert.FromBase64String(encKeyStr);
            if (encKeyBytes.Length <= 5) return null;

            // First 5 bytes are "DPAPI" prefix — strip them, then DPAPI-unprotect
            var dpapiBlob = encKeyBytes[5..];
            var key = ProtectedData.Unprotect(dpapiBlob, null, DataProtectionScope.CurrentUser);

            _cache.EncryptionKey[localStatePath] = key;
            return key;
        }
        catch (Exception ex)
        {
            _log.LogDebug("Failed to load encryption key from {path}: {msg}", localStatePath, ex.Message);
            return null;
        }
    }

    // ── AES-GCM decryption ────────────────────────────────────────────────────

    private static string? DecryptV10(byte[] key, byte[] blob)
    {
        // Format: "v10" (3 bytes) | nonce (12 bytes) | ciphertext | tag (16 bytes)
        if (blob.Length < 31) return null;
        if (blob[0] != (byte)'v' || blob[1] != (byte)'1' || blob[2] != (byte)'0') return null;

        var nonce      = blob[3..15];                           // 12 bytes
        var tag        = blob[(blob.Length - 16)..];            // last 16 bytes
        var ciphertext = blob[15..(blob.Length - 16)];          // middle

        try
        {
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsGitIpcSecret(string key)
    {
        // key format: secret://{"extensionId":"vscode.git","key":"git-ipc-auth-token:..."}
        return key.Contains("\"vscode.git\"") && key.Contains("git-ipc-auth-token");
    }

    private static (string? ExtensionId, string? SecretKey) ParseSecretKey(string key)
    {
        // Strip "secret://" prefix
        if (!key.StartsWith("secret://", StringComparison.Ordinal)) return (null, null);
        var json = key["secret://".Length..];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var extId  = root.TryGetProperty("extensionId", out var e) ? e.GetString() : null;
            var secKey = root.TryGetProperty("key",         out var k) ? k.GetString() : null;
            return (extId, secKey);
        }
        catch
        {
            return (null, null);
        }
    }

    private static byte[]? ParseBufferJson(string value)
    {
        // Format: {"type":"Buffer","data":[1,2,3,...]}
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "Buffer") return null;
            if (!root.TryGetProperty("data", out var data)) return null;

            // Pre-size from array length to avoid intermediate JsonElement[] allocation.
            var bytes = new byte[data.GetArrayLength()];
            int idx = 0;
            foreach (var e in data.EnumerateArray())
                bytes[idx++] = (byte)e.GetInt32();
            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
