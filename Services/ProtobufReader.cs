using CodepostEx.Models;

namespace CodepostEx.Services;

public sealed class ProtobufReader
{
    private static readonly System.Text.RegularExpressions.Regex PrintableRe =
        new(@"[\x20-\x7E]{10,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public IReadOnlyList<ChatEntry> ExtractChats(string pbPath, string workspaceHash, DateTime? since)
    {
        if (!File.Exists(pbPath)) return [];

        try
        {
            var bytes   = File.ReadAllBytes(pbPath);
            var content = System.Text.Encoding.UTF8.GetString(bytes);

            var parts = PrintableRe.Matches(content)
                .Select(m => m.Value)
                .Where(s => s.Length >= 10)
                .ToList();

            if (parts.Count == 0) return [];

            var combined = string.Join(" ", parts);
            return
            [
                new ChatEntry(
                    WorkspaceHash: workspaceHash,
                    FolderUri:     null,
                    Id:            Guid.NewGuid().ToString(),
                    Timestamp:     DateTimeOffset.UtcNow,
                    UserMessage:   combined,
                    AiResponse:    "",
                    IsSensitive:   false)
            ];
        }
        catch
        {
            return [];
        }
    }
}
