using DoItNow.Models;
using DoItNow.Services;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DoItNow.Tests;

public class ProfileServiceTests
{
    [Fact]
    public void CreateProfile_PersistsNameAndReservesDatabaseFolder()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila");

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("Camila", profile.DisplayName);
        Assert.False(profile.HasPassword);
        Assert.True(profile.CreatedAt > DateTime.MinValue);

        var databasePath = service.GetDatabasePath(profile);
        Assert.StartsWith(env.ProfileRoot, databasePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.GetDirectoryName(databasePath)));

        // Otro servicio lee el mismo registro desde disco.
        var reloaded = Assert.Single(new ProfileService().GetProfiles());
        Assert.Equal(profile.Id, reloaded.Id);
        Assert.Equal("Camila", reloaded.DisplayName);
    }

    [Fact]
    public void CreateProfile_TrimsNameAndRejectsDuplicatesIgnoringCase()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("  Andrea  ");
        Assert.Equal("Andrea", profile.DisplayName);

        Assert.Throws<InvalidOperationException>(() => service.CreateProfile("andrea"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" x ")]
    [InlineData("Este nombre de perfil es demasiado largo para la UI")]
    public void CreateProfile_RejectsInvalidNames(string name)
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        Assert.Throws<ArgumentException>(() => service.CreateProfile(name));
    }

    [Fact]
    public void CreateProfile_RejectsPasswordUnderMinimumLength()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var tooShort = new string('a', ProfileService.MinPasswordLength - 1);

        Assert.Throws<ArgumentException>(() => service.CreateProfile("Demo", tooShort));
    }

    [Fact]
    public void CreateProfile_StoresOnlySaltAndVerifier()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        const string password = "claveSecretaDelPerfil!";
        var profile = service.CreateProfile("Camila", password);

        Assert.True(profile.HasPassword);
        Assert.False(string.IsNullOrWhiteSpace(profile.PasswordSalt));
        Assert.False(string.IsNullOrWhiteSpace(profile.PasswordVerifier));
        Assert.Equal(CryptoService.DefaultIterations, profile.PasswordIterations);

        var registry = File.ReadAllText(Path.Combine(env.ProfileRoot, "profiles.json"));
        Assert.DoesNotContain(password, registry);
        Assert.Contains("passwordSalt", registry);
        Assert.Contains("passwordVerifier", registry);
    }

    [Fact]
    public void VerifyPassword_AcceptsCorrectAndRejectsWrong()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila", "clave correcta");

        Assert.True(service.VerifyPassword(profile, "clave correcta"));
        Assert.False(service.VerifyPassword(profile, "clave incorrecta"));
        Assert.False(service.VerifyPassword(profile, string.Empty));
        Assert.False(service.VerifyPassword(profile, null));
    }

    [Fact]
    public void VerifyPassword_WithoutPassword_AcceptsAnyValue()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Invitado");

        Assert.True(service.VerifyPassword(profile, null));
        Assert.True(service.VerifyPassword(profile, string.Empty));
        Assert.True(service.VerifyPassword(profile, "lo que sea"));
    }

    [Fact]
    public void ChangePassword_InvalidatesTheOldOne()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila", "primera clave");
        service.ChangePassword(profile, "segunda clave");

        Assert.False(service.VerifyPassword(profile, "primera clave"));
        Assert.True(service.VerifyPassword(profile, "segunda clave"));

        var reloaded = Assert.Single(new ProfileService().GetProfiles());
        Assert.True(service.VerifyPassword(reloaded, "segunda clave"));
    }

    [Fact]
    public void ChangePassword_RejectsShortPassword()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila", "clave valida");

        Assert.Throws<ArgumentException>(() => service.ChangePassword(profile, "corta"));
    }

    [Fact]
    public void RemovePassword_ReturnsToPasswordlessAccess()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila", "clave valida");
        service.RemovePassword(profile);

        Assert.False(profile.HasPassword);
        Assert.True(service.VerifyPassword(profile, null));

        var reloaded = Assert.Single(new ProfileService().GetProfiles());
        Assert.False(reloaded.HasPassword);
        Assert.True(service.VerifyPassword(reloaded, "cualquiera"));
    }

    [Fact]
    public void MarkLogin_UpdatesTimestampAndLastProfile()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var first = service.CreateProfile("Primero");
        var second = service.CreateProfile("Segundo");

        service.MarkLogin(first);
        Assert.Equal(first.Id, service.GetLastProfile()?.Id);
        Assert.NotNull(service.GetProfiles().Single(p => p.Id == first.Id).LastLoginAt);

        service.MarkLogin(second);
        Assert.Equal(second.Id, service.GetLastProfile()?.Id);
        Assert.Equal(second.Id, service.GetProfiles()[0].Id);
    }

    [Fact]
    public void DeleteProfile_RemovesRegistryEntryAndData()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila", "clave valida");
        var folder = Path.GetDirectoryName(service.GetDatabasePath(profile))!;
        File.WriteAllText(Path.Combine(folder, "doitnow.db"), "datos simulados");

        service.DeleteProfile(profile, deleteData: true);

        Assert.Empty(service.GetProfiles());
        Assert.Null(service.FindByName("Camila"));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void DeleteProfile_WithoutData_KeepsDatabaseFolder()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila");
        var folder = Path.GetDirectoryName(service.GetDatabasePath(profile))!;

        service.DeleteProfile(profile, deleteData: false);

        Assert.Empty(service.GetProfiles());
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void CreateFirstProfile_MigratesLegacyDatabase()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        Directory.CreateDirectory(env.ProfileRoot);
        const string legacyContent = "base anterior al sistema de perfiles";
        var legacyPath = Path.Combine(env.ProfileRoot, "doitnow.db");
        File.WriteAllText(legacyPath, legacyContent);

        var profile = service.CreateProfile("Camila");

        var migratedPath = service.GetDatabasePath(profile);
        Assert.True(File.Exists(migratedPath));
        Assert.Equal(legacyContent, File.ReadAllText(migratedPath));
        Assert.False(File.Exists(legacyPath));
    }

    [Fact]
    public void CreateSecondProfile_DoesNotStealMigratedDatabase()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        Directory.CreateDirectory(env.ProfileRoot);
        File.WriteAllText(Path.Combine(env.ProfileRoot, "doitnow.db"), "datos");

        var first = service.CreateProfile("Primera");
        var second = service.CreateProfile("Segunda");

        Assert.True(File.Exists(service.GetDatabasePath(first)));
        Assert.False(File.Exists(service.GetDatabasePath(second)));
    }

    [Fact]
    public void GetProfiles_RecoversFoldersWhenRegistryIsCorrupted()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        var profile = service.CreateProfile("Camila");
        File.WriteAllText(Path.Combine(env.ProfileRoot, "profiles.json"), "{ esto no es json");

        var recovered = Assert.Single(new ProfileService().GetProfiles());

        Assert.Equal(profile.Id, recovered.Id);
        Assert.StartsWith("Perfil ", recovered.DisplayName);
        Assert.False(recovered.HasPassword);
    }

    [Fact]
    public void FindByName_IgnoresCaseAndOuterSpaces()
    {
        using var env = new TestEnvironment();
        var service = new ProfileService();

        service.CreateProfile("Camila");

        Assert.NotNull(service.FindByName("camila"));
        Assert.NotNull(service.FindByName("  CAMILA "));
        Assert.Null(service.FindByName("Otra"));
        Assert.Null(service.FindByName(""));
    }
}
