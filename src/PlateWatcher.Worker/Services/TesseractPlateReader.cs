using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlateWatcher.Models;
using PlateWatcher.Options;
using Tesseract;

namespace PlateWatcher.Services;

public sealed class TesseractPlateReader : IPlateReader, IDisposable
{
    private readonly ILogger<TesseractPlateReader> _logger;
    private readonly OcrOptions _options;
    private readonly PlateTextExtractor _extractor;
    private readonly TesseractEngine _engine;

    public TesseractPlateReader(
        IOptions<OcrOptions> options,
        PlateTextExtractor extractor,
        ILogger<TesseractPlateReader> logger)
    {
        _options = options.Value;
        _logger = logger;
        _extractor = extractor;
        _engine = new TesseractEngine(ResolvePath(_options.TessDataPath), _options.Language, EngineMode.Default);
    }

    public Task<PlateRecognitionResult> ReadAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                using var pix = Pix.LoadFromMemory(imageBytes);
                var rawTextBuilder = new System.Text.StringBuilder();

                foreach (var pageSegMode in _options.PageSegModes)
                {
                    using var page = _engine.Process(
                        pix,
                        (PageSegMode)pageSegMode);

                    var rawText = page.GetText();
                    rawTextBuilder.AppendLine(rawText);
                    var confidence = page.GetMeanConfidence() * 100;

                    var candidates = _extractor.Extract(rawText);
                    if (candidates.Count > 0)
                    {
                        _logger.LogDebug(
                            "OCR psm={PageSegMode} candidatos={Candidates} confianza={Confidence:F1}%",
                            pageSegMode,
                            string.Join(", ", candidates),
                            confidence);

                        return PlateRecognitionResult.Found(candidates[0], candidates, confidence, rawText);
                    }
                }

                var fullText = rawTextBuilder.ToString();
                _logger.LogWarning("No se detectó ninguna matrícula. Texto OCR: {RawText}", fullText);
                return PlateRecognitionResult.NotFound(fullText);
            },
            cancellationToken);
    }

    public void Dispose() => _engine.Dispose();

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
