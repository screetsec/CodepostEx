using System.Collections.Concurrent;

namespace CodepostEx.Core;

public sealed class AppCache
{
    public ConcurrentDictionary<string, byte[]> EncryptionKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<string>                TempCopyPaths { get; } = [];

    public void Cleanup()
    {
        foreach (var path in TempCopyPaths)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
