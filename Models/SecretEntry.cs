namespace CodepostEx.Models;

public record SecretEntry(
    string  Tool,
    string  ExtensionId,
    string  SecretKey,
    string? Value,
    bool    Decrypted,
    string  SourceKey,
    string  Database);
