using DoItNow.Data;
using DoItNow.Services;
using DoItNow.Services.Drive;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class DriveBackupCoordinatorTests
{
    private static async Task<VaultService> CreateUnlockedVaultAsync()
    {
        var vault = new VaultService();
        await vault.InitializeAsync();
        Assert.True(vault.IsUnlocked);
        return vault;
    }

    [Fact]
    public async Task SaveCredentials_StoresClientSecretEncrypted()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();
        var settings = new SettingsService();
        var coordinator = new DriveBackupCoordinator(vault, settings);

        Assert.False(await coordinator.HasCredentialsAsync());

        await coordinator.SaveCredentialsAsync("client-id.apps.googleusercontent.com", "super-secret");

        Assert.True(await coordinator.HasCredentialsAsync());

        var (clientId, clientSecret) = await coordinator.GetCredentialsAsync();
        Assert.Equal("client-id.apps.googleusercontent.com", clientId);
        Assert.Equal("super-secret", clientSecret);

        await using var db = new AppDbContext();
        var stored = await db.Settings.FindAsync(DriveBackupCoordinator.KeyClientSecret);
        Assert.NotNull(stored);
        Assert.NotEqual("super-secret", stored!.Value);
        Assert.DoesNotContain("super-secret", stored.Value);
    }

    [Fact]
    public async Task ChangingClientId_DisconnectsAccount()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();
        var settings = new SettingsService();
        var coordinator = new DriveBackupCoordinator(vault, settings);

        await coordinator.SaveCredentialsAsync("client-uno", "secret-uno");
        await settings.SetSettingAsync(
            DriveBackupCoordinator.KeyRefreshToken, SecretProtector.Protect("refresh-uno"));

        Assert.True(await coordinator.IsConnectedAsync());

        await coordinator.SaveCredentialsAsync("client-dos", "secret-dos");

        Assert.False(await coordinator.IsConnectedAsync());
    }

    [Fact]
    public async Task SavingSameClientId_KeepsSession()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();
        var settings = new SettingsService();
        var coordinator = new DriveBackupCoordinator(vault, settings);

        await coordinator.SaveCredentialsAsync("client-uno", "secret-uno");
        await settings.SetSettingAsync(
            DriveBackupCoordinator.KeyRefreshToken, SecretProtector.Protect("refresh-uno"));

        await coordinator.SaveCredentialsAsync("client-uno", "secret-dos");

        Assert.True(await coordinator.IsConnectedAsync());
    }

    [Fact]
    public async Task Disconnect_ClearsRefreshToken()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();
        var settings = new SettingsService();
        var coordinator = new DriveBackupCoordinator(vault, settings);

        await coordinator.SaveCredentialsAsync("client-uno", "secret-uno");
        await settings.SetSettingAsync(
            DriveBackupCoordinator.KeyRefreshToken, SecretProtector.Protect("refresh-uno"));

        await coordinator.DisconnectAsync();

        Assert.False(await coordinator.IsConnectedAsync());
        Assert.Equal(string.Empty, await settings.GetSettingAsync(DriveBackupCoordinator.KeyRefreshToken));
    }

    [Fact]
    public async Task ListAsync_WithoutConnection_Throws()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();
        var coordinator = new DriveBackupCoordinator(vault, new SettingsService());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ListAsync());

        Assert.Contains("Conecta tu cuenta", error.Message);
    }

    [Fact]
    public async Task ExpiredRefreshToken_DisconnectsAndShowsFriendlyMessage()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();
        var settings = new SettingsService();

        var handler = new FakeHttpHandler().Enqueue(
            () => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}",
                    Encoding.UTF8,
                    "application/json")
            });

        var auth = new GoogleDriveAuth(new HttpClient(handler));
        var coordinator = new DriveBackupCoordinator(vault, settings, auth);

        await coordinator.SaveCredentialsAsync("client-uno", "secret-uno");
        await settings.SetSettingAsync(
            DriveBackupCoordinator.KeyRefreshToken, SecretProtector.Protect("refresh-vencido"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ListAsync());

        Assert.Contains("sesión de Google expiró", error.Message);
        Assert.False(await coordinator.IsConnectedAsync());
    }

    [Fact]
    public void SecretProtector_RoundTrips()
    {
        var protectedValue = SecretProtector.Protect("mi-secreto");

        Assert.NotEqual("mi-secreto", protectedValue);
        Assert.Equal("mi-secreto", SecretProtector.Unprotect(protectedValue));
        Assert.Equal(string.Empty, SecretProtector.Unprotect(string.Empty));
        Assert.Equal(string.Empty, SecretProtector.Unprotect("no-es-base64"));
    }
}
