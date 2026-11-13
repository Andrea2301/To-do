using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Tests;

/// <summary>Petición HTTP capturada por <see cref="FakeHttpHandler"/>.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method, string Url, string? BearerToken, string? Body);

/// <summary>
/// Handler HTTP falso: devuelve respuestas encoladas y registra cada petición.
/// Permite probar los clientes de Drive sin red ni credenciales reales.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public FakeHttpHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    public FakeHttpHandler Enqueue(Func<HttpResponseMessage> responder) =>
        Enqueue(_ => responder());

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri?.OriginalString ?? string.Empty,
            request.Headers.Authorization?.Parameter,
            body));

        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeHttpHandler: no hay respuesta encolada.");

        return _responses.Dequeue()(request);
    }
}
