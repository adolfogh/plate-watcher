using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlateWatcher.Models;
using PlateWatcher.Options;
using PlateWatcher.Services;
using Xunit;

namespace PlateWatcher.Tests;

public class RestPlateSenderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public Func<string, HttpResponseMessage> Responder { get; set; } =
            static _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return Responder(LastBody ?? string.Empty);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StaticOptionsStore<T> : IOptionsMonitor<T>
    {
        public StaticOptionsStore(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static (RestPlateSender Sender, StubHandler Handler) CreateSender(ExternalApiOptions? options = null)
    {
        var handler = new StubHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.ejemplo.com/") };
        var sender = new RestPlateSender(
            new StubHttpClientFactory(client),
            new StaticOptionsStore<ExternalApiOptions>(options ?? new ExternalApiOptions
            {
                BaseUrl = "https://api.ejemplo.com",
                ApiKey = "secreto-prueba",
            }),
            NullLogger<RestPlateSender>.Instance);

        return (sender, handler);
    }

    [Fact]
    public async Task SendAsync_EnviaJsonConApiKey()
    {
        var (sender, handler) = CreateSender();
        var observation = new PlateObservation(
            "1234-ABC",
            new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            "C:/fotos/003.jpg",
            "003.jpg");

        var result = await sender.SendAsync(observation, imageBytes: null);

        Assert.True(result.Success);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-API-Key", out var values));
        Assert.Equal("secreto-prueba", Assert.Single(values));
        Assert.Contains("\"matricula\":\"1234-ABC\"", handler.LastBody);
        Assert.Contains("\"archivoOrigen\":\"003.jpg\"", handler.LastBody);
        Assert.DoesNotContain("imagenBase64", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_SinApiKey_FallaSinLlamarHttp()
    {
        var (sender, handler) = CreateSender(new ExternalApiOptions
        {
            BaseUrl = "https://api.ejemplo.com",
            ApiKey = "",
        });

        var result = await sender.SendAsync(
            new PlateObservation("1234-ABC", DateTime.UtcNow, "C:/fotos/003.jpg", "003.jpg"),
            imageBytes: null);

        Assert.False(result.Success);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SendAsync_RespuestaRechazada_DevuelveError()
    {
        var (sender, handler) = CreateSender();
        handler.Responder = static _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("invalid key"),
        };

        var result = await sender.SendAsync(
            new PlateObservation("1234-ABC", DateTime.UtcNow, "C:/fotos/003.jpg", "003.jpg"),
            imageBytes: null);

        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
    }
}
