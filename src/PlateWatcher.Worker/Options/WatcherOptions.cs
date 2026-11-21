using System.ComponentModel.DataAnnotations;

namespace PlateWatcher.Options;

public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    [Required]
    public string InputFolder { get; set; } = "input";

    public string ProcessedFolder { get; set; } = "processed";

    public string FailedFolder { get; set; } = "failed";

    public string FilePattern { get; set; } = "*.jpg";

    public int MaxDegreeOfParallelism { get; set; } = 1;

    public int FileRetryAttempts { get; set; } = 5;

    public int FileRetryDelaySeconds { get; set; } = 2;

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;
}
