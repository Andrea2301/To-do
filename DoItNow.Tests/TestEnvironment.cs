using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace DoItNow.Tests;

/// <summary>
/// Aísla cada test en una base SQLite temporal mediante DOITNOW_DB_PATH.
/// Evita tocar la base real del usuario y restaura la variable al terminar.
/// </summary>
internal sealed class TestEnvironment : IDisposable
{
    private readonly string? _previousPath;
    private readonly string _folder;

    public TestEnvironment()
    {
        _previousPath = Environment.GetEnvironmentVariable("DOITNOW_DB_PATH");

        _folder = Path.Combine(Path.GetTempPath(), "DoItNow.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        Use("primary.db");
    }

    public string Folder => _folder;

    /// <summary>Cambia la base activa (por ejemplo, para probar exportar en una e importar en otra).</summary>
    public string Use(string fileName)
    {
        var path = Path.Combine(_folder, fileName);
        Environment.SetEnvironmentVariable("DOITNOW_DB_PATH", path);
        return path;
    }

    public string File(string fileName) => Path.Combine(_folder, fileName);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOITNOW_DB_PATH", _previousPath);
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Un archivo temporal bloqueado no debe hacer fallar el test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
