namespace CodepostEx.Models;

public record TrustEntry(
    string FsPath,
    bool   Trusted,
    string Database
);
