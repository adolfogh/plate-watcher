using System.ComponentModel.DataAnnotations;

namespace PlateWatcher.Options;

public sealed class ExternalApiOptions
{
    public const string SectionName = "ExternalApi";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public string Endpoint { get; set; } = "api/placas";

    public string ApiKeyHeader { get; set; } = "X-API-Key";

    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public bool IncludeImage { get; set; } = false;
}
