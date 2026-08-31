using System.Text.RegularExpressions;
using CodepostEx.Models;

namespace CodepostEx.Services;

public sealed class CredentialScanner
{
    private static readonly (string Name, string Category, Regex Pattern)[] Patterns =
    [
        ("Amazon_AWS_Access_Key_ID",       "Cloud",         Rx(@"([^A-Z0-9]|^)(AKIA|A3T|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|ASIA)[A-Z0-9]{12,}")),
        ("AWS_S3_Bucket",                  "Cloud",         Rx(@"//s3-[a-z0-9-]+\.amazonaws\.com/[a-z0-9._-]+|//s3\.amazonaws\.com/[a-z0-9._-]+|amzn\.mws\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")),
        ("AWS_API_Key",                    "Cloud",         Rx(@"AKIA[0-9A-Z]{16}")),
        ("Google_API_Key",                 "Cloud",         Rx(@"AIza[0-9A-Za-z\-_]{35}")),
        ("Google_OAuth_Access_Token",      "Cloud",         Rx(@"ya29\.[0-9A-Za-z\-_]+")),
        ("Google_Cloud_Platform_OAuth",    "Cloud",         Rx(@"[0-9]+-[0-9A-Za-z_]{32}\.apps\.googleusercontent\.com")),
        ("Google_Cloud_Service_Account",   "Cloud",         Rx(@"""type""\s*:\s*""service_account""")),
        ("Firebase",                       "Cloud",         Rx(@"[a-z0-9.-]+\.firebaseio\.com")),
        ("Heroku_API_Key",                 "Cloud",         Rx(@"[hH][eE][rR][oO][kK][uU].*[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}")),
        ("Azure_Storage_Key",              "Cloud",         Rx(@"DefaultEndpointsProtocol=https;AccountName=[^;]+;AccountKey=[a-zA-Z0-9+/=]{88}")),
        ("Artifactory_API_Token",          "Cloud",         Rx(@"(?:\s|=|:|,|\""|^)AKC[a-zA-Z0-9]{10,}")),
        ("Artifactory_Password",           "Cloud",         Rx(@"(?:\s|=|:|,|\""|^)AP[\dABCDEF][a-zA-Z0-9]{8,}")),
        ("Cloudinary_Basic_Auth",          "Cloud",         Rx(@"cloudinary://[0-9]{15}:[a-zA-Z0-9_\-]+@[a-z]+")),
        ("MongoDB",                        "Database",      Rx(@"mongodb(?:\+srv)?://(?:[^:]+:[^@]+@)?[^\s\x22'<>]+")),
        ("PostgreSQL",                     "Database",      Rx(@"postgres(?:ql)?://(?:[^:]+:[^@]+@)?[^\s\x22'<>]+")),
        ("MySQL",                          "Database",      Rx(@"mysql://(?:[^:]+:[^@]+@)?[^\s\x22'<>]+")),
        ("Redis",                          "Database",      Rx(@"redis://(?:[^:]+:[^@]+@)?[^\s\x22'<>]+")),
        ("Cassandra",                      "Database",      Rx(@"cassandra://(?:[^:]+:[^@]+@)?[^\s\x22'<>]+")),
        ("Discord_BOT_Token",              "Social",        Rx(@"((?:N|M|O)[a-zA-Z0-9]{23}\.[a-zA-Z0-9\-_]{6}\.[a-zA-Z0-9\-_]{27})")),
        ("Facebook_Access_Token",          "Social",        Rx(@"EAACEdEose0cBA[0-9A-Za-z]+")),
        ("Facebook_OAuth",                 "Social",        Rx(@"[fF][aA][cC][eE][bB][oO][oO][kK].*['\x22][0-9a-f]{32}['\x22]")),
        ("Facebook_ClientID",             "Social",        Rx(@"(?:facebook|fb)(?:_|-|\.)?(?:client|app)?(?:_|-|\.)?id['\x22\s]*(?::|=)['\x22\s]*([0-9]{13,17})")),
        ("Facebook_Secret_Key",           "Social",        Rx(@"(?:facebook|fb)(?:_|-|\.)?(?:client|app)?(?:_|-|\.)?secret['\x22\s]*(?::|=)['\x22\s]*([0-9a-f]{32})")),
        ("Twitter_Access_Token",           "Social",        Rx(@"[tT][wW][iI][tT][tT][eE][rR].*[1-9][0-9]+-[0-9a-zA-Z]{40}")),
        ("Twitter_OAuth",                  "Social",        Rx(@"[tT][wW][iI][tT][tT][eE][rR].*['\x22][0-9a-zA-Z]{35,44}['\x22]")),
        ("Twitter_ClientID",              "Social",        Rx(@"(?:twitter)(?:_|-|\.)?(?:client|consumer)?(?:_|-|\.)?(?:id|key)['\x22\s]*(?::|=)['\x22\s]*([a-zA-Z0-9]{18,25})")),
        ("Twitter_Secret_Key",            "Social",        Rx(@"(?:twitter)(?:_|-|\.)?(?:client|consumer)?(?:_|-|\.)?secret['\x22\s]*(?::|=)['\x22\s]*([a-zA-Z0-9]{35,45})")),
        ("Slack_Token",                    "Social",        Rx(@"(xox[pboa]-[0-9]{12}-[0-9]{12}-[0-9]{12}-[a-z0-9]{32})")),
        ("Slack_Webhook",                  "Social",        Rx(@"https://hooks\.slack\.com/services/T[a-zA-Z0-9_]{8}/B[a-zA-Z0-9_]{8}/[a-zA-Z0-9_]{24}")),
        ("GitHub_Token",                   "Development",   Rx(@"[gG][iI][tT][hH][uU][bB].*['\x22][0-9a-zA-Z]{35,40}['\x22]")),
        ("GitHub_Access_Token",            "Development",   Rx(@"([a-zA-Z0-9_\-]*:[a-zA-Z0-9_\-]+@github\.com*)")),
        ("Stripe_API_Key",                 "Payment",       Rx(@"sk_live_[0-9a-zA-Z]{24}")),
        ("Stripe_Restricted_API_Key",      "Payment",       Rx(@"rk_live_[0-9a-zA-Z]{24}")),
        ("PayPal_Braintree_Access_Token",  "Payment",       Rx(@"access_token\$production\$[0-9a-z]{16}\$[0-9a-f]{32}")),
        ("Square_Access_Token",            "Payment",       Rx(@"sq0atp-[0-9A-Za-z\-_]{22}")),
        ("Square_OAuth_Secret",            "Payment",       Rx(@"sq0csp-[0-9A-Za-z\-_]{43}")),
        ("Picatic_API_Key",                "Payment",       Rx(@"sk_live_[0-9a-z]{32}")),
        ("Twilio_API_Key",                 "Communication", Rx(@"SK[0-9a-fA-F]{32}")),
        ("Mailgun_API_Key",                "Communication", Rx(@"key-[0-9a-zA-Z]{32}")),
        ("MailChimp_API_Key",              "Communication", Rx(@"[0-9a-f]{32}-us[0-9]{1,2}")),
        ("JSON_Web_Token",                 "Generic",       Rx(@"(?=.*[a-z])(?=.*[0-9])(?:[a-z0-9_=]+\.){2}(?:[a-z0-9_\-\+\/=]*)")),
        ("Generic_API_Key",                "Generic",       Rx(@"[aA][pP][iI][_]?[kK][eE][yY].*['\x22][0-9a-zA-Z]{32,45}['\x22]")),
        ("Generic_Secret",                 "Generic",       Rx(@"[sS][eE][cC][rR][eE][tT].*['\x22][0-9a-zA-Z]{32,45}['\x22]")),
        ("OpenAI_API_Key",                 "AI",            Rx(@"sk-[a-zA-Z0-9]{20}T3BlbkFJ[a-zA-Z0-9]{20}")),
        ("OpenAI_Project_Key",             "AI",            Rx(@"sk-proj-[a-zA-Z0-9_\-]{80,}")),
        ("Anthropic_API_Key",              "AI",            Rx(@"sk-ant-[a-zA-Z0-9_\-]{80,}")),
        ("Private_Key",                    "Generic",       Rx(@"-----BEGIN (RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY(?: BLOCK)?-----")),
        ("Password_in_URL",               "Generic",       Rx(@"[a-zA-Z]{3,10}://[^/\s:@""']{3,20}:[^/\s:@""']{3,20}@.{1,100}")),
        ("Basic_Auth_Credentials",        "Generic",       Rx(@"(?:https?://)[^:\s""']+:[^@\s""']{4,}@[a-zA-Z0-9._\-]+")),
        ("Basic_Auth",                     "Generic",       Rx(@"basic\s+[a-zA-Z0-9+/=]{20,}")),
        ("Bearer_Token",                   "Generic",       Rx(@"bearer\s+[a-zA-Z0-9._\-]{20,}")),
    ];

    private static readonly Regex EmailPattern =
        new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SkipExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".html", ".js", ".css", ".zip" };

    private static Regex Rx(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<CredentialFinding> ScanDirectoryFreeText(string dirPath, string term)
    {
        if (!Directory.Exists(dirPath)) return [];

        var results = new List<CredentialFinding>();
        foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            if (SkipExtensions.Contains(Path.GetExtension(file))) continue;
            try
            {
                var content = File.ReadAllText(file, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content)) continue;

                int idx = 0;
                var name = Path.GetFileName(file);
                while ((idx = content.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int start = Math.Max(0, idx - 40);
                    int end   = Math.Min(content.Length, idx + term.Length + 40);
                    var snippet = content[start..end].Replace('\n', ' ').Replace('\r', ' ');
                    results.Add(new CredentialFinding("FreeText", term, name, snippet));
                    idx += term.Length;
                    if (results.Count > 1000) return results; // cap
                }
            }
            catch { }
        }
        return results;
    }

    public IReadOnlyList<CredentialFinding> ScanDirectory(string dirPath, bool credentials = true, bool emails = true)
    {
        if (!Directory.Exists(dirPath))
            return [];

        var results = new List<CredentialFinding>();
        foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            if (SkipExtensions.Contains(Path.GetExtension(file)))
                continue;

            try
            {
                var content = File.ReadAllText(file, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var name = Path.GetFileName(file);
                if (credentials)
                    results.AddRange(ScanCredentials(content, name));
                if (emails)
                    results.AddRange(ScanEmails(content, name));
            }
            catch { }
        }

        return results;
    }

    private static IEnumerable<CredentialFinding> ScanCredentials(string content, string fileName)
    {
        foreach (var (typeName, category, pattern) in Patterns)
        {
            foreach (Match m in pattern.Matches(content))
            {
                // Find first successful capture group; fall back to whole match
                var val = m.Value;
                for (int g = 1; g < m.Groups.Count; g++)
                {
                    if (m.Groups[g].Success) { val = m.Groups[g].Value; break; }
                }

                if (val.Length > 80) val = val[..77] + "...";
                yield return new CredentialFinding(category, typeName, fileName, val);
            }
        }
    }

    private static IEnumerable<CredentialFinding> ScanEmails(string content, string fileName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in EmailPattern.Matches(content))
        {
            var email = m.Value.ToLowerInvariant();
            if (seen.Add(email))
                yield return new CredentialFinding("Email", "Email", fileName, email);
        }
    }
}
