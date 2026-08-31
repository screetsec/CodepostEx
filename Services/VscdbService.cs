using CodepostEx.Core;
using Microsoft.Data.Sqlite;

namespace CodepostEx.Services;

public sealed class VscdbService
{
    private readonly AppCache _cache;

    public VscdbService(AppCache cache) => _cache = cache;

    public SqliteConnection? OpenReadOnly(string dbPath)
    {
        if (!File.Exists(dbPath)) return null;

        try
        {
            var conn = MakeConnection(dbPath);
            conn.Open();
            return conn;
        }
        catch
        {
            // DB locked by the running IDE — copy to temp then open
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"codepost_{Guid.NewGuid():N}.vscdb");
            try
            {
                File.Copy(dbPath, tempPath, overwrite: true);
                _cache.TempCopyPaths.Add(tempPath);
                var conn = MakeConnection(tempPath);
                conn.Open();
                return conn;
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                return null;
            }
        }
    }

    public string? GetValue(string dbPath, string key)
    {
        using var conn = OpenReadOnly(dbPath);
        return conn is null ? null : GetValue(conn, key);
    }

    public static string? GetValue(SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM ItemTable WHERE key = $k LIMIT 1";
        cmd.Parameters.AddWithValue("$k", key);
        var result = cmd.ExecuteScalar();
        return result is string s && s.Length > 0 ? s : null;
    }

    public static IEnumerable<(string Key, string Value)> GetByKeyPattern(
        SqliteConnection conn, string likePattern)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM ItemTable WHERE key LIKE $p AND typeof(value) = 'text'";
        cmd.Parameters.AddWithValue("$p", likePattern);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1)) continue;
            yield return (reader.GetString(0), reader.GetString(1));
        }
    }

    public static IEnumerable<(string Key, string Value)> GetAllTextRows(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM ItemTable WHERE typeof(value) = 'text' AND length(value) > 10";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1)) continue;
            yield return (reader.GetString(0), reader.GetString(1));
        }
    }

    private static SqliteConnection MakeConnection(string path) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString);
}
