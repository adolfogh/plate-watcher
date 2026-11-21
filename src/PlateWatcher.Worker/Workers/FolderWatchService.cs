using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PlateWatcher.Models;
using PlateWatcher.Options;
using PlateWatcher.Services;

namespace PlateWatcher.Workers;

public sealed record PendingImage(string FilePath, int Attempt);

public sealed class FolderWatchService : BackgroundService
{
    private readonly ILogger<FolderWatchService> _logger;
    private readonly WatcherOptions _options;
    private readonly IPlateReader _reader;
    private readonly IPlateSender _sender;
    private readonly Channel<PendingImage> _queue =
        Channel.CreateUnbounded<PendingImage>(new UnboundedChannelOptions { SingleReader = false });

    public FolderWatchService(
        IOptions<WatcherOptions> options,
        IPlateReader reader,
        IPlateSender sender,
        ILogger<FolderWatchService> logger)
    {
        _options = options.Value;
        _reader = reader;
        _sender = sender;
        _logger = logger;
    }

    private string InputPath => Resolve(_options.InputFolder);
    private string ProcessedPath => Resolve(_options.ProcessedFolder);
    private string FailedPath => Resolve(_options.FailedFolder);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(InputPath);
        Directory.CreateDirectory(ProcessedPath);
        Directory.CreateDirectory(FailedPath);

        _logger.LogInformation("Vigilando {Input}. Procesadas en {Processed}, fallidas en {Failed}",
            InputPath, ProcessedPath, FailedPath);

        EnqueueExistingFiles();

        using var watcher = CreateWatcher();

        var consumers = Enumerable
            .Range(0, Math.Max(1, _options.MaxDegreeOfParallelism))
            .Select(_ => Task.Run(() => ConsumeAsync(stoppingToken), stoppingToken))
            .ToArray();

        await Task.WhenAll(consumers);
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(InputPath, _options.FilePattern)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };

        watcher.Created += static (_, e) => Enqueue(e.FullPath, 0);
        watcher.Renamed += static (_, e) => Enqueue(e.FullPath, 0);
        watcher.Error += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error");

        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void Enqueue(string filePath, int attempt)
    {
        if (!_queue.Writer.TryWrite(new PendingImage(filePath, attempt)))
        {
            _logger.LogWarning("No se pudo encolar {File}", filePath);
        }
    }

    private void EnqueueExistingFiles()
    {
        var files = Directory
            .EnumerateFiles(InputPath, _options.FilePattern, SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            Enqueue(file, 0);
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado procesando {File}", item.FilePath);
                MoveTo(item.FilePath, FailedPath, "error inesperado");
            }
        }
    }

    private async Task ProcessAsync(PendingImage item, CancellationToken stoppingToken)
    {
        if (!File.Exists(item.FilePath))
        {
            return;
        }

        if (!await IsFileReadyAsync(item.FilePath))
        {
            if (item.Attempt < _options.FileRetryAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.FileRetryDelaySeconds), stoppingToken);
                await _queue.Writer.WriteAsync(new PendingImage(item.FilePath, item.Attempt + 1), stoppingToken);
            }
            else
            {
                MoveTo(item.FilePath, FailedPath, "el archivo no quedó liberado");
            }

            return;
        }

        var bytes = await File.ReadAllBytesAsync(item.FilePath, stoppingToken);

        if (bytes.Length == 0 || bytes.Length > _options.MaxFileSizeBytes)
        {
            MoveTo(item.FilePath, FailedPath, "tamaño de archivo inválido");
            return;
        }

        var recognition = await _reader.ReadAsync(bytes, stoppingToken);

        if (!recognition.Success || recognition.Plate is null)
        {
            _logger.LogWarning("Sin matrícula reconocible en {File}", item.FilePath);
            MoveTo(item.FilePath, FailedPath, "matrícula no reconocida");
            return;
        }

        var observation = new PlateObservation(
            recognition.Plate,
            File.GetLastWriteTimeUtc(item.FilePath),
            item.FilePath,
            Path.GetFileName(item.FilePath));

        var dispatch = await _sender.SendAsync(observation, bytes, stoppingToken);

        if (dispatch.Success)
        {
            _logger.LogInformation(
                "Matrícula {Plate} enviada correctamente (HTTP {StatusCode}) desde {File} [candidatos: {Candidates}]",
                observation.Plate,
                dispatch.StatusCode,
                item.FilePath,
                string.Join(", ", recognition.Candidates));

            MoveTo(item.FilePath, ProcessedPath, null);
        }
        else
        {
            _logger.LogWarning(
                "Fallo al enviar la matrícula {Plate}: {Error}",
                observation.Plate,
                dispatch.Error);

            MoveTo(item.FilePath, FailedPath, dispatch.Error);
        }
    }

    private static async Task<bool> IsFileReadyAsync(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void MoveTo(string sourcePath, string targetFolder, string? reason)
    {
        try
        {
            var fileName = Path.GetFileName(sourcePath);
            var targetPath = Path.Combine(targetFolder, fileName);

            if (File.Exists(targetPath))
            {
                var name = Path.GetFileNameWithoutExtension(fileName);
                var extension = Path.GetExtension(fileName);
                targetPath = Path.Combine(targetFolder, $"{name}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}");
            }

            File.Move(sourcePath, targetPath);

            if (reason is not null)
            {
                _logger.LogWarning("Movido {File} a {Target}: {Reason}", sourcePath, targetPath, reason);
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "No se pudo mover {File} a {Target}", sourcePath, targetFolder);
        }
    }

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
