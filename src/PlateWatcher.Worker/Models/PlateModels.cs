namespace PlateWatcher.Models;

public sealed record PlateObservation(
    string Plate,
    DateTime CapturedAtUtc,
    string SourceFilePath,
    string SourceFileName);

public sealed record PlateRecognitionResult(
    bool Success,
    string? Plate,
    IReadOnlyList<string> Candidates,
    double Confidence,
    string RawText)
{
    public static PlateRecognitionResult Found(
        string plate,
        IReadOnlyList<string> candidates,
        double confidence,
        string rawText) =>
        new(true, plate, candidates, confidence, rawText);

    public static PlateRecognitionResult NotFound(string rawText) =>
        new(false, null, [], 0, rawText);
}

public sealed record PlateDispatchResult(
    bool Success,
    int? StatusCode,
    string? Error)
{
    public static PlateDispatchResult Successful(int statusCode) =>
        new(true, statusCode, null);

    public static PlateDispatchResult Rejected(int statusCode, string error) =>
        new(false, statusCode, error);

    public static PlateDispatchResult Failed(string error) =>
        new(false, null, error);
}

public sealed record PlatePayload(
    string Matricula,
    DateTime FechaCapturaUtc,
    string ArchivoOrigen,
    string? ImagenBase64);
