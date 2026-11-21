namespace PlateWatcher.Options;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public string TessDataPath { get; set; } = "tessdata";

    public string Language { get; set; } = "spa";

    public string PlateRegex { get; set; } =
        @"\b[0-9]{4}[- ]?[A-Z]{3}\b|\b[A-Z]{1,2}[0-9]{3}[ -]?[A-Z]{2}\b|\b[A-Z]{1,3}-?[0-9]{4,6}\b|\b[0-9]{6,8}\b";

    public int[] PageSegModes { get; set; } = [6, 11];
}
