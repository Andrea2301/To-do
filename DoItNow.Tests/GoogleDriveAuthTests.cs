using DoItNow.Services.Drive;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class GoogleDriveAuthTests
{
    [Fact]
    public void CreateCodeChallenge_MatchesRfc7636Vector()
    {
        // Vector de prueba del RFC 7636, apéndice B.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            GoogleDriveAuth.CreateCodeChallenge(verifier));
    }

    [Fact]
    public void CreateCodeVerifier_IsUrlSafeAndLongEnough()
    {
        var verifier = GoogleDriveAuth.CreateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.DoesNotContain('=', verifier);
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.NotEqual(verifier, GoogleDriveAuth.CreateCodeVerifier());
    }

    [Fact]
    public void Base64Url_RemovesPaddingAndUsesUrlAlphabet()
    {
        var encoded = GoogleDriveAuth.Base64Url(new byte[] { 0xFB, 0xFF, 0xBF });

        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.Equal("-_-_", encoded);
    }

    [Fact]
    public void BuildAuthorizeUrl_ContainsPkceAndOfflineParams()
    {
        var url = GoogleDriveAuth.BuildAuthorizeUrl(
            "client-123", "http://127.0.0.1:4444", "challenge-abc", "state-xyz");

        Assert.StartsWith(GoogleDriveAuth.AuthorizeEndpoint + "?", url);

        var query = GoogleDriveAuth.ParseQuery(url);
        Assert.Equal("client-123", query["client_id"]);
        Assert.Equal("http://127.0.0.1:4444", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(GoogleDriveAuth.DriveAppDataScope, query["scope"]);
        Assert.Equal("challenge-abc", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("state-xyz", query["state"]);
    }

    [Fact]
    public void BuildAuthorizeUrl_EscapesValues()
    {
        var url = GoogleDriveAuth.BuildAuthorizeUrl(
            "id con espacio", "http://127.0.0.1:1/?a=b", "c", "s");

        Assert.Contains("client_id=id%20con%20espacio", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A1%2F%3Fa%3Db", url);
    }

    [Fact]
    public void ParseQuery_DecodesValuesAndPlusAsSpace()
    {
        var query = GoogleDriveAuth.ParseQuery("/?code=4%2F0Abc&state=x%2By&scope=a+b&flag");

        Assert.Equal("4/0Abc", query["code"]);
        Assert.Equal("x+y", query["state"]);
        Assert.Equal("a b", query["scope"]);
        Assert.Equal(string.Empty, query["flag"]);
    }

    [Fact]
    public void ParseQuery_WithoutQueryString_ReturnsEmpty()
    {
        Assert.Empty(GoogleDriveAuth.ParseQuery("/"));
        Assert.Empty(GoogleDriveAuth.ParseQuery("/?"));
    }

    [Fact]
    public void ParseToken_ComputesExpiryFromExpiresIn()
    {
        var now = DateTimeOffset.UtcNow;
        var token = GoogleDriveAuth.ParseToken(
            "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":1800," +
            "\"scope\":\"drive.appdata\"}", now);

        Assert.Equal("at", token.AccessToken);
        Assert.Equal("rt", token.RefreshToken);
        Assert.Equal("drive.appdata", token.Scope);
        Assert.Equal(now.AddSeconds(1800), token.ExpiresAt);
        Assert.False(token.IsExpired);
    }

    [Fact]
    public void ParseToken_DefaultsToOneHourAndNoRefreshToken()
    {
        var now = DateTimeOffset.UtcNow;
        var token = GoogleDriveAuth.ParseToken("{\"access_token\":\"at\"}", now);

        Assert.Equal(string.Empty, token.RefreshToken);
        Assert.Equal(now.AddSeconds(3600), token.ExpiresAt);
    }

    [Fact]
    public void ParseToken_ExpiredWithinMinuteMargin()
    {
        var token = GoogleDriveAuth.ParseToken(
            "{\"access_token\":\"at\",\"expires_in\":30}", DateTimeOffset.UtcNow);

        Assert.True(token.IsExpired);
    }

    [Fact]
    public void ParseToken_WithoutAccessToken_ThrowsGoogleMessage()
    {
        var error = Assert.Throws<GoogleAuthException>(() =>
            GoogleDriveAuth.ParseToken(
                "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}",
                DateTimeOffset.UtcNow));

        Assert.Contains("expired or revoked", error.Message);
        Assert.Equal("invalid_grant", error.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_SendsFormAndParsesToken()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"nuevo\",\"expires_in\":3600}",
                Encoding.UTF8,
                "application/json")
        });

        var auth = new GoogleDriveAuth(new HttpClient(handler));
        var token = await auth.RefreshAsync("id", "secret", "refresh-1");

        Assert.Equal("nuevo", token.AccessToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(GoogleDriveAuth.TokenEndpoint, request.Url);
        Assert.Contains("grant_type=refresh_token", request.Body);
        Assert.Contains("refresh_token=refresh-1", request.Body);
        Assert.Contains("client_id=id", request.Body);
        Assert.Contains("client_secret=secret", request.Body);
    }

    [Fact]
    public async Task RefreshAsync_WithErrorResponse_ThrowsDescription()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}",
                Encoding.UTF8,
                "application/json")
        });

        var auth = new GoogleDriveAuth(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<GoogleAuthException>(
            () => auth.RefreshAsync("id", "secret", "refresh-1"));

        Assert.Contains("expired or revoked", error.Message);
        Assert.Equal("invalid_grant", error.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_WithoutDescription_UsesErrorCodeAsMessage()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":\"invalid_client\"}", Encoding.UTF8, "application/json")
        });

        var auth = new GoogleDriveAuth(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<GoogleAuthException>(
            () => auth.RefreshAsync("id", "secret", "refresh-1"));

        Assert.Equal("invalid_client", error.ErrorCode);
        Assert.Equal("invalid_client", error.Message);
    }
}
