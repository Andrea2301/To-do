using DoItNow.Models;
using DoItNow.Services;
using System;
using Xunit;

namespace DoItNow.Tests;

public class AppSessionTests
{
    private static AppProfile Profile() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Camila"
    };

    [Fact]
    public void SignIn_ExposesActiveProfileAndDatabase()
    {
        var profile = Profile();

        AppSession.SignIn(profile, @"C:\datos\perfil\doitnow.db");

        Assert.True(AppSession.IsSignedIn);
        Assert.Same(profile, AppSession.CurrentProfile);
        Assert.Equal(@"C:\datos\perfil\doitnow.db", AppSession.DatabasePath);

        AppSession.SignOut();
    }

    [Fact]
    public void SignOut_ClearsTheSession()
    {
        AppSession.SignIn(Profile(), @"C:\datos\perfil\doitnow.db");

        AppSession.SignOut();

        Assert.False(AppSession.IsSignedIn);
        Assert.Null(AppSession.CurrentProfile);
        Assert.Null(AppSession.DatabasePath);
    }

    [Fact]
    public void Initial_UsesFirstLetterOrFallback()
    {
        Assert.Equal("C", new AppProfile { DisplayName = "camila" }.Initial);
        Assert.Equal("?", new AppProfile { DisplayName = "   " }.Initial);
    }
}
