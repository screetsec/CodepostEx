namespace CodepostEx.Models;

public record ChatEntry(
    string          WorkspaceHash,
    string?         FolderUri,
    string          Id,
    DateTimeOffset? Timestamp,
    string          UserMessage,
    string          AiResponse,
    bool            IsSensitive
);
