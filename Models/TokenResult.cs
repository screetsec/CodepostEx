namespace CodepostEx.Models;

public record TokenResult(
    string         Tool,
    string         TokenType,
    string         SourceKey,
    string         Value,
    string         Database)
{
    public TokenAnalysis? Analysis { get; init; }
}

public record TokenAnalysis(
    string          TokenFormat,
    string?         Algorithm,
    string?         Subject,
    string?         Issuer,
    string?         Audience,
    string?         SessionType,
    string?         Scope,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? ExpiresAt,
    bool?           IsExpired,
    string          ValidationStatus,
    string?         NetworkValidation);
