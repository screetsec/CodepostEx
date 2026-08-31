namespace CodepostEx.Core;

public sealed class PathResolver
{
    public string AppData     { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string LocalAppData{ get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string UserProfile { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Temp        { get; } = Path.GetTempPath();

    public string Resolve(string appDataSubPath)      => Path.Combine(AppData,      appDataSubPath);
    public string ResolveLocal(string subPath)        => Path.Combine(LocalAppData, subPath);
    public string ResolveUser(string userProfileSub)  => Path.Combine(UserProfile,  userProfileSub);

    public string? FindExecutable(string fileName)
    {
        var ext = Path.GetExtension(fileName).Length == 0 ? ".exe" : string.Empty;
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return pathVar.Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir, fileName + ext))
            .FirstOrDefault(File.Exists);
    }
}
