using PlateWatcher.Options;
using PlateWatcher.Services;
using Xunit;

namespace PlateWatcher.Tests;

public class PlateTextExtractorTests
{
    private static PlateTextExtractor CreateExtractor() =>
        new(new OcrOptions());

    [Theory]
    [InlineData("ENTRADA CAMION 1234-ABC PUERTA 2", "1234-ABC")]
    [InlineData("PLACA 1234 ABC", "1234ABC")]
    [InlineData("MATRICULA ABC-1234 OBSERVACION", "ABC-1234")]
    [InlineData("AB123CD FRONTERA", "AB123CD")]
    [InlineData("VERIFICAR 98765432 INMEDIATAMENTE", "98765432")]
    public void Extract_DevuelveCandidatosValidos(string rawText, string expected)
    {
        var extractor = CreateExtractor();

        var candidates = extractor.Extract(rawText);

        Assert.Contains(expected, candidates);
    }

    [Fact]
    public void Extract_ConTextoVacio_NoDevuelveCandidatos()
    {
        var extractor = CreateExtractor();

        var candidates = extractor.Extract("   ");

        Assert.Empty(candidates);
    }

    [Fact]
    public void Extract_ConConfusionesOcr_CorrigeYDetecta()
    {
        var extractor = CreateExtractor();

        var candidates = extractor.Extract("L234-ABC");

        Assert.Contains("1234-ABC", candidates);
    }

    [Fact]
    public void Extract_EliminaDuplicados()
    {
        var extractor = CreateExtractor();

        var candidates = extractor.Extract("1234-ABC 1234-ABC DEF");

        Assert.Equal(1, candidates.Count);
        Assert.Equal("1234-ABC", candidates[0]);
    }
}
