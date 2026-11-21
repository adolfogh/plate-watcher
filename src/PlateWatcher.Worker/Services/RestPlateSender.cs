using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlateWatcher.Models;
using PlateWatcher.Options;

namespace PlateWatcher.Services;

public sealed class RestPlateSender : IPlateSender
{
    public const string ClientName = "ExternalPlateApi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<ExternalApiOptions> _options;
    private readonly ILogger<RestPlateSender> _logger;

    public RestPlateSender(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ExternalApiOptions> options,
        ILogger<RestPlateSender> logger)
    {
        _httpClient = httpClientFactory.CreateClient(ClientName);
        _options = options;
        _logger = logger;
    }

    public async Task<PlateDispatchResult> SendAsync(
        PlateObservation observation,
        byte[]? imageBytes,
        CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;

        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out _))
        {
            return PlateDispatchResult.Failed("ExternalApi:BaseUrl no es una URL válida.");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return PlateDispatchResult.Failed("ExternalApi:ApiKey no está configurada.");
        }

        var payload = new PlatePayload(
            observation.Plate,
            observation.CapturedAtUtc,
            observation.SourceFileName,
            config.IncludeImage && imageBytes is { Length: > 0 }
                ? Convert.ToBase64String(imageBytes)
                : null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint);
            request.Headers.Add(config.ApiKeyHeader, config.ApiKey);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return PlateDispatchResult.Successful((int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "La API externa rechazó la matrícula {Plate}: {StatusCode} {Body}",
                observation.Plate,
                (int)response.StatusCode,
                body);

            return PlateDispatchResult.Rejected((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de red enviando la matrícula {Plate}", observation.Plate);
            return PlateDispatchResult.Failed(ex.Message);
        }
    }
}
