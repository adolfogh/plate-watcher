using PlateWatcher.Models;

namespace PlateWatcher.Services;

public interface IPlateReader
{
    Task<PlateRecognitionResult> ReadAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
