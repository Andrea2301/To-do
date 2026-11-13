using DoItNow.Services;
using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace DoItNow.Tests;

/// <summary>
/// Aísla cada test en una base SQLite temporal mediante DOITNOW_DB_PATH y en un
/// registro de perfiles temporal mediante DOITNOW_PROFILE_ROOT.
/// Evita tocar los datos reales del usuario y restaura las variables al terminar.
/// </summary>
internal sealed class TestEnvironment : IDisposable
{
    private readonly string? _previousPath;
    private readonly string? _previousProfileRoot;
    private readonly string _folder;

    public TestEnvironment()
    {
        _previousPath = Environment.GetEnvironmentVariable("DOITNOW_DB_PATH");
        _previousProfileRoot = Environment.GetEnvironmentVariable("DOITNOW_PROFILE_ROOT");

        _folder = Path.Combine(Path.GetTempPath(), "DoItNow.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        Environment.SetEnvironmentVariable(
            "DOITNOW_PROFILE_ROOT", Path.Combine(_folder, "profile-root"));

        Use("primary.db");
    }

    public string Folder => _folder;

    /// <summary>Raíz del registro de perfiles usada por ProfileService.</summary>
    public string ProfileRoot => ProfileService.RootDirectory;

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
        Environment.SetEnvironmentVariable("DOITNOW_PROFILE_ROOT", _previousProfileRoot);
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
