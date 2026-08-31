namespace CodepostEx.Models;

public static class IdeProfiles
{
    private static readonly string Home =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static readonly IdeProfile[] All =
    [
        new(
            Name:                     "Cursor",
            AppDataSubPath:           @"Cursor\User",
            AlternateAppDataSubPaths: [],
            HistorySubPath:           @"Cursor\User\History",
            SettingsSubPath:          @"Cursor\User\settings.json",
            GlobalStorageSubPath:     @"Cursor\User\globalStorage\state.vscdb",
            WorkspaceStorageSubPath:  @"Cursor\User\workspaceStorage",
            AbsoluteChatStoragePath:  null,
            LocalStateSubPath:        @"Cursor\Local State",
            TokenKeys:                ["cursorAuth/accessToken", "cursorAuth/refreshToken"],
            StorageType:              IdeStorageType.Sqlite,
            SentrySubPath:            @"Cursor\sentry\scope_v3.json"
        ),
        new(
            Name:                     "Windsurf",
            AppDataSubPath:           @"Windsurf\User",
            AlternateAppDataSubPaths: [],
            HistorySubPath:           @"Windsurf\User\History",
            SettingsSubPath:          @"Windsurf\User\settings.json",
            GlobalStorageSubPath:     @"Windsurf\User\globalStorage\state.vscdb",
            WorkspaceStorageSubPath:  null,
            AbsoluteChatStoragePath:  Path.Combine(Home, @".codeium\windsurf\cascade"),
            LocalStateSubPath:        @"Windsurf\Local State",
            TokenKeys:                [],
            StorageType:              IdeStorageType.Protobuf
        ),
        new(
            Name:                     "Kiro",
            AppDataSubPath:           @"Kiro\User",
            AlternateAppDataSubPaths: [],
            HistorySubPath:           @"Kiro\User\History",
            SettingsSubPath:          @"Kiro\User\settings.json",
            GlobalStorageSubPath:     @"Kiro\User\globalStorage\state.vscdb",
            WorkspaceStorageSubPath:  @"Kiro\User\workspaceStorage",
            AbsoluteChatStoragePath:  null,
            LocalStateSubPath:        @"Kiro\Local State",
            TokenKeys:                [],
            StorageType:              IdeStorageType.Sqlite
        ),
        new(
            Name:                     "Trae",
            AppDataSubPath:           @"Trae\User",
            AlternateAppDataSubPaths: [@"trae\User"],
            HistorySubPath:           @"Trae\User\History",
            SettingsSubPath:          @"Trae\User\settings.json",
            GlobalStorageSubPath:     @"Trae\User\globalStorage\state.vscdb",
            WorkspaceStorageSubPath:  @"Trae\User\workspaceStorage",
            AbsoluteChatStoragePath:  null,
            LocalStateSubPath:        @"Trae\Local State",
            TokenKeys:                [],
            StorageType:              IdeStorageType.Sqlite,
            SentrySubPath:            null
        ),
        new(
            Name:                     "Antigravity",
            AppDataSubPath:           @"Antigravity\User",
            AlternateAppDataSubPaths: [@"Antigravity IDE\User"],
            HistorySubPath:           null,
            SettingsSubPath:          @"Antigravity\User\settings.json",
            GlobalStorageSubPath:     @"Antigravity\User\globalStorage\state.vscdb",
            WorkspaceStorageSubPath:  @"Antigravity\User\workspaceStorage",
            AbsoluteChatStoragePath:  Path.Combine(Home, @".gemini\antigravity-ide\brain"),
            LocalStateSubPath:        @"Antigravity\Local State",
            TokenKeys:                ["antigravityUnifiedStateSync.oauthToken"],
            StorageType:              IdeStorageType.AntigravityJsonl
        ),
    ];

    public static IdeProfile? Find(string name) =>
        All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<IdeProfile> Filter(string toolFilter) =>
        toolFilter.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? All
            : All.Where(p => p.Name.Equals(toolFilter, StringComparison.OrdinalIgnoreCase));
}
