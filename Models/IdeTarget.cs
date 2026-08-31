namespace CodepostEx.Models;

public record IdeTarget(
    IdeProfile Profile,
    string?    ExecutablePath,
    string     AppDataPath,
    string     GlobalStoragePath,
    string     LocalStatePath,
    string?    WorkspaceStoragePath
)
{
    public string Name => Profile.Name;
}
