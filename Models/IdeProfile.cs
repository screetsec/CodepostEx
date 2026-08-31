namespace CodepostEx.Models;

public enum IdeStorageType { Sqlite, Protobuf, AntigravityJsonl }

public record IdeProfile(
    string         Name,
    string         AppDataSubPath,
    string[]       AlternateAppDataSubPaths,
    string?        HistorySubPath,
    string         SettingsSubPath,
    string         GlobalStorageSubPath,
    string?        WorkspaceStorageSubPath,
    string?        AbsoluteChatStoragePath,
    string         LocalStateSubPath,
    string[]       TokenKeys,
    IdeStorageType StorageType,
    string?        SentrySubPath = null     // e.g. Cursor\sentry\scope_v3.json
);
