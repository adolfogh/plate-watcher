using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PlateWatcher.Options;
using PlateWatcher.Services;
using PlateWatcher.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<WatcherOptions>()
    .Bind(builder.Configuration.GetSection(WatcherOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<OcrOptions>()
    .Bind(builder.Configuration.GetSection(OcrOptions.SectionName));

builder.Services
    .AddOptions<ExternalApiOptions>()
    .Bind(builder.Configuration.GetSection(ExternalApiOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => Uri.IsWellFormedUriString(options.BaseUrl, UriKind.Absolute),
        "ExternalApi:BaseUrl debe ser una URL absoluta.")
    .ValidateOnStart();

builder.Services.AddHttpClient(RestPlateSender.ClientName, (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<ExternalApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
}).AddStandardResilienceHandler();

builder.Services.AddSingleton<PlateTextExtractor>(sp =>
    new PlateTextExtractor(sp.GetRequiredService<IOptions<OcrOptions>>().Value));
builder.Services.AddSingleton<IPlateReader, TesseractPlateReader>();
builder.Services.AddSingleton<IPlateSender, RestPlateSender>();
builder.Services.AddHostedService<FolderWatchService>();

var host = builder.Build();
host.Run();
