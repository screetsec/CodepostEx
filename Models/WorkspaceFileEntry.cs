namespace CodepostEx.Models;

public record WorkspaceFileEntry(
    string  Path,
    string  Content,
    string? Language,
    long    Size,
    long?   Timestamp
);
