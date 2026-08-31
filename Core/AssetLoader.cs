using System.Reflection;
using System.Text;

namespace CodepostEx.Core;

public static class AssetLoader
{
    private static readonly Assembly Asm = typeof(AssetLoader).Assembly;

    public static string Load(string name)
    {
        using var stream = OpenStream(name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static byte[] LoadBytes(string name)
    {
        using var stream = OpenStream(name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static Stream OpenStream(string name)
    {
        // Convert path separators to dots: Assets/payloads/tasks.json -> CodepostEx.Assets.payloads.tasks.json
        var resourceName = $"CodepostEx.Assets.{name.Replace('/', '.').Replace('\\', '.')}";
        return Asm.GetManifestResourceStream(resourceName)
               ?? throw new InvalidOperationException($"Embedded asset not found: {resourceName}");
    }
}
