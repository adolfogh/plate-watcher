using System.Text.RegularExpressions;
using PlateWatcher.Options;

namespace PlateWatcher.Services;

public sealed class PlateTextExtractor
{
    private static readonly (char From, char To)[] Ambiguities =
    [
        ('O', '0'),
        ('Q', '0'),
        ('I', '1'),
        ('L', '1'),
        ('S', '5'),
        ('B', '8'),
        ('Z', '2'),
    ];

    private readonly Regex _plateRegex;

    public PlateTextExtractor(OcrOptions options)
    {
        _plateRegex = new Regex(options.PlateRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    public IReadOnlyList<string> Extract(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return [];
        }

        var normalized = Normalize(rawText);
        var candidates = CollectCandidates(normalized);

        if (candidates.Count == 0)
        {
            candidates = CollectCandidates(Disambiguate(normalized));
        }

        return candidates;
    }

    public static string Normalize(string rawText)
    {
        var builder = new System.Text.StringBuilder(rawText.Length);

        foreach (var character in rawText.ToUpperInvariant())
        {
            if (char.IsWhiteSpace(character))
            {
                builder.Append(' ');
            }
            else if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else
            {
                builder.Append(character is '.' or ',' or '·' ? ' ' : '-');
            }
        }

        return builder.ToString();
    }

    private static string Disambiguate(string normalized)
    {
        var result = normalized;

        foreach (var (from, to) in Ambiguities)
        {
            result = result.Replace(from, to);
        }

        return result;
    }

    private List<string> CollectCandidates(string normalized)
    {
        var candidates = new List<string>();

        foreach (Match match in _plateRegex.Matches(normalized))
        {
            var candidate = match.Value.Replace(" ", string.Empty, StringComparison.Ordinal);

            if (!candidates.Contains(candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }
}
