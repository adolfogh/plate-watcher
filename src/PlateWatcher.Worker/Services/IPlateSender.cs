using PlateWatcher.Models;

namespace PlateWatcher.Services;

public interface IPlateSender
{
    Task<PlateDispatchResult> SendAsync(
        PlateObservation observation,
        byte[]? imageBytes,
        CancellationToken cancellationToken = default);
}
