using PurchaseMemory.Application.Products;

namespace PurchaseMemory.IntegrationTests;

public sealed class ProductNameNormalizerTests
{
    [Theory]
    [InlineData("Ser Gouda 250g", "ser gouda 250 g")]
    [InlineData("SER GOUDA 250G", "ser gouda 250 g")]
    [InlineData("  ser   gouda\t250 g  ", "ser gouda 250 g")]
    [InlineData("Ser Gouda, 250 g.", "ser gouda 250 g")]
    public void NormalizeFoldsCaseWhitespaceAndPunctuation(string input, string expected)
    {
        Assert.Equal(expected, ProductNameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Mleko Łaciate 3,2%", "mleko laciate 3 2")]
    [InlineData("MLEKO ŁACIATE 3.2%", "mleko laciate 3 2")]
    [InlineData("Chleb żytni", "chleb zytni")]
    [InlineData("Jajka wiejskie ŚWIEŻE", "jajka wiejskie swieze")]
    [InlineData("Ćwikła z chrzanem", "cwikla z chrzanem")]
    [InlineData("Sól kamienna", "sol kamienna")]
    [InlineData("Gęś pieczona", "ges pieczona")]
    [InlineData("Źrebięcina", "zrebiecina")]
    [InlineData("Żurek", "zurek")]
    [InlineData("Grüße", "grusse")]
    [InlineData("GRÜẞE", "grusse")]
    public void NormalizeFoldsPolishDiacritics(string input, string expected)
    {
        Assert.Equal(expected, ProductNameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("150g", "150 g")]
    [InlineData("150 g", "150 g")]
    [InlineData("1,5L", "1 5 l")]
    public void NormalizeSeparatesDigitsFromLetters(string input, string expected)
    {
        Assert.Equal(expected, ProductNameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***")]
    public void NormalizeReturnsAnEmptyKeyWhenNothingUsableRemains(string? input)
    {
        Assert.Equal(string.Empty, ProductNameNormalizer.Normalize(input));
    }

    [Fact]
    public void IdenticalWordingInDifferentCasesShareOneKey()
    {
        Assert.Equal(
            ProductNameNormalizer.Normalize("Ser Gouda 250g"),
            ProductNameNormalizer.Normalize("SER GOUDA 250G"));
    }

    [Fact]
    public void SimilarityIsSymmetricAndBounded()
    {
        var left = ProductNameNormalizer.Normalize("Ser Gouda plastry 150 g");
        var right = ProductNameNormalizer.Normalize("Chleb żytni razowy");

        Assert.Equal(1.0, ProductSimilarity.Score(left, left));
        Assert.Equal(ProductSimilarity.Score(left, right), ProductSimilarity.Score(right, left));
        Assert.InRange(ProductSimilarity.Score(left, right), 0, ProductMatchingThresholds.PlausibleCandidate);
    }

    [Fact]
    public void DifferentPackageSizesAreDetectedEvenWhenTheTextIsAlmostIdentical()
    {
        var left = ProductNameNormalizer.Normalize("Ser Gouda plastry 150 g");
        var right = ProductNameNormalizer.Normalize("Ser Gouda plastry 250 g");

        Assert.False(ProductSimilarity.NumericSignaturesMatch(left, right));
    }
}
