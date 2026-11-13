using DoItNow.Services.Drive;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class GoogleDriveClientTests
{
    private const string ListJson =
        "{\"files\":[" +
        "{\"id\":\"a1\",\"name\":\"doitnow-vault-20260101-101010.ditvault\"," +
        "\"size\":\"2048\",\"modifiedTime\":\"2026-01-01T10:10:10.000Z\"}," +
        "{\"id\":\"a2\",\"name\":\"notas.txt\",\"size\":\"10\",\"modifiedTime\":\"2026-01-02T10:10:10.000Z\"}" +
        "]}";

    private static GoogleDriveClient CreateClient(
        FakeHttpHandler handler, Func<string>? token = null, Action? invalidate = null)
    {
        token ??= () => "token-1";

        return new GoogleDriveClient(
            new HttpClient(handler),
            _ => Task.FromResult(token()),
            invalidate);
    }

    [Fact]
    public async Task ListAsync_FiltersVaultFilesAndParsesMetadata()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ListJson, Encoding.UTF8, "application/json")
        });

        var client = CreateClient(handler);
        var files = await client.ListAsync();

        var file = Assert.Single(files);
        Assert.Equal("a1", file.Id);
        Assert.Equal("doitnow-vault-20260101-101010.ditvault", file.Name);
        Assert.Equal(2048, file.SizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 10, 10, TimeSpan.Zero), file.ModifiedAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("spaces=appDataFolder", request.Url);
        Assert.Contains("orderBy=modifiedTime%20desc", request.Url);
        Assert.Equal("token-1", request.BearerToken);
    }

    [Fact]
    public async Task ListAsync_WithoutFilesProperty_ReturnsEmpty()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        Assert.Empty(await CreateClient(handler).ListAsync());
    }

    [Fact]
    public async Task UploadAsync_StartsResumableSessionAndPutsContent()
    {
        const string response =
            "{\"id\":\"nuevo\",\"name\":\"doitnow-vault-20260101-101010.ditvault\"," +
            "\"size\":\"5\",\"modifiedTime\":\"2026-01-01T10:10:10.000Z\"}";

        var handler = new FakeHttpHandler()
            .Enqueue(() =>
            {
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Location = new Uri("https://upload.example/session/abc");
                return start;
            })
            .Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler);
        var content = Encoding.UTF8.GetBytes("vault");
        var file = await client.UploadAsync("doitnow-vault-20260101-101010.ditvault", content);

        Assert.Equal("nuevo", file.Id);
        Assert.Equal(5, file.SizeBytes);

        Assert.Equal(2, handler.Requests.Count);

        var start = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, start.Method);
        Assert.Contains("uploadType=resumable", start.Url);
        Assert.Contains("appDataFolder", start.Body);
        Assert.Contains("doitnow-vault-20260101-101010.ditvault", start.Body);
        Assert.Equal("token-1", start.BearerToken);

        var put = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://upload.example/session/abc", put.Url);
        Assert.Equal("vault", put.Body);
    }

    [Fact]
    public async Task UploadAsync_WithoutSessionUri_Throws()
    {
        var handler = new FakeHttpHandler().Enqueue(
            () => new HttpResponseMessage(HttpStatusCode.OK));

        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.UploadAsync("backup.ditvault", Array.Empty<byte>()));

        Assert.Contains("URL de carga", error.Message);
    }

    [Fact]
    public async Task UploadAsync_WithoutFileName_Throws()
    {
        var client = CreateClient(new FakeHttpHandler());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.UploadAsync("  ", Array.Empty<byte>()));
    }

    [Fact]
    public async Task DownloadAsync_RequestsMediaAndReturnsBytes()
    {
        var payload = Encoding.UTF8.GetBytes("contenido cifrado");
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });

        var result = await CreateClient(handler).DownloadAsync("id/with space");

        Assert.Equal(payload, result);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/files/id%2Fwith%20space", request.Url);
        Assert.Contains("alt=media", request.Url);
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteToFileResource()
    {
        var handler = new FakeHttpHandler().Enqueue(
            () => new HttpResponseMessage(HttpStatusCode.NoContent));

        await CreateClient(handler).DeleteAsync("abc");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith("/files/abc", request.Url);
    }

    [Fact]
    public async Task UnauthorizedResponse_RefreshesTokenAndRetriesOnce()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(() => new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ListJson, Encoding.UTF8, "application/json")
            });

        var tokenCalls = 0;
        var invalidations = 0;
        var client = CreateClient(
            handler,
            () => $"token-{++tokenCalls}",
            () => invalidations++);

        var files = await client.ListAsync();

        Assert.Single(files);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, invalidations);
        Assert.Equal("token-1", handler.Requests[0].BearerToken);
        Assert.Equal("token-2", handler.Requests[1].BearerToken);
    }

    [Fact]
    public async Task UnauthorizedTwice_Throws()
    {
        var handler = new FakeHttpHandler()
            .Enqueue(() => new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .Enqueue(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListAsync());
    }

    [Fact]
    public async Task ErrorResponse_UsesGoogleMessage()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":403,\"message\":\"The user has not granted the app access.\"}}",
                Encoding.UTF8,
                "application/json")
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateClient(handler).ListAsync());

        Assert.Contains("The user has not granted the app access.", error.Message);
    }

    [Fact]
    public async Task ErrorResponse_WithoutJson_UsesStatusCode()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateClient(handler).ListAsync());

        Assert.Contains("500", error.Message);
    }
}
