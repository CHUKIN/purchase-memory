using System.Text;
using PurchaseMemory.ProductBackfill;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The backfill tool rewrites whatever database it is handed, so these cases
/// describe what it must refuse. Every fixture is built inside a temporary
/// directory — the repository's real <c>data/</c> is never read or written.
/// </summary>
public sealed class ProductionDatabaseGuardTests : IDisposable
{
    private readonly string _root;

    public ProductionDatabaseGuardTests()
    {
        // A non-ASCII segment on purpose: it reproduces the NFC/NFD mismatch that
        // silently disabled the repository rule on the owner's machine.
        _root = Path.Combine(
            Path.GetTempPath(),
            "purchase-memory-guard-tests",
            Guid.NewGuid().ToString("N"),
            "Учёт GPT");

        Directory.CreateDirectory(Path.Combine(_root, "data", "backups"));
        Directory.CreateDirectory(Path.Combine(_root, "tools", "backfill"));
        Directory.CreateDirectory(Path.Combine(_root, "scratch"));
        File.WriteAllText(
            Path.Combine(_root, ProductionDatabaseGuard.RepositoryMarkerFileName),
            "<Solution />");
    }

    [Fact]
    public void TheLiveDatabaseIsRefused()
    {
        var live = CreateFile("data", "purchases.db");

        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(live, DataDirectory));
    }

    [Fact]
    public void TheFileNameRuleAloneCatchesADataDirectoryOutsideAnyRepository()
    {
        var live = CreateFile("data", "purchases.db");

        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(live, repositoryDataDirectory: null));
    }

    [Theory]
    [InlineData("purchases.db-wal")]
    [InlineData("notes.db")]
    public void AnyOtherFileInsideTheRepositoryDataDirectoryIsRefused(string fileName)
    {
        var path = CreateFile("data", fileName);

        // This is the rule that a Unicode-form mismatch used to disable outright.
        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(path, DataDirectory));
    }

    [Fact]
    public void ANestedBackupInsideTheRepositoryDataDirectoryIsRefused()
    {
        var backup = CreateFile(Path.Combine("data", "backups"), "copy-2026-09-01.db");

        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(backup, DataDirectory));
    }

    [Fact]
    public void ADotDotTraversalCannotDisguiseTheLiveDatabase()
    {
        var disguised = Path.Combine(_root, "tools", "..", "data", "purchases.db");
        CreateFile("data", "purchases.db");

        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(disguised, DataDirectory));
    }

    [Fact]
    public void ASymlinkedFilePointingAtTheLiveDatabaseIsRefused()
    {
        var live = CreateFile("data", "purchases.db");
        var link = Path.Combine(_root, "scratch", "innocent-name.db");
        File.CreateSymbolicLink(link, live);

        // The link's own name and directory look harmless; only the target does not.
        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(link, DataDirectory));
    }

    [Fact]
    public void ASymlinkedDirectoryPointingAtTheDataDirectoryIsRefused()
    {
        CreateFile("data", "purchases.db");
        var linkedDirectory = Path.Combine(_root, "scratch", "linked");
        Directory.CreateSymbolicLink(linkedDirectory, DataDirectory);

        var throughLink = Path.Combine(linkedDirectory, "purchases.db");

        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(throughLink, DataDirectory));
    }

    [Fact]
    public void TheRepositoryRuleSurvivesADifferentUnicodeFormOnEitherSide()
    {
        var path = CreateFile("data", "notes.db");

        // macOS hands the runtime a decomposed path («ё» as e + U+0308) while a
        // typed argument arrives composed. Comparing code units made the whole
        // repository rule silently dead on a repository named «Учёт GPT».
        var decomposedRoot = DataDirectory.Normalize(NormalizationForm.FormD);
        var composedPath = path.Normalize(NormalizationForm.FormC);

        Assert.NotEqual(decomposedRoot, DataDirectory.Normalize(NormalizationForm.FormC));
        Assert.True(ProductionDatabaseGuard.IsProductionDatabase(composedPath, decomposedRoot));
    }

    [Fact]
    public void AGenuineScratchDatabaseIsAllowed()
    {
        var scratch = CreateFile("scratch", "copy-for-testing.db");

        Assert.False(ProductionDatabaseGuard.IsProductionDatabase(scratch, DataDirectory));
    }

    [Fact]
    public void TheRepositoryDataDirectoryIsFoundByWalkingUpToTheMarkerFile()
    {
        var found = ProductionDatabaseGuard.FindRepositoryDataDirectory(
            Path.Combine(_root, "tools", "backfill"));

        Assert.NotNull(found);
        Assert.Equal(
            ProductionDatabaseGuard.ResolveRealDirectory(DataDirectory),
            found);
    }

    [Fact]
    public void NoRepositoryAboveThePathMeansNoRepositoryRule()
    {
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);

        try
        {
            Assert.Null(ProductionDatabaseGuard.FindRepositoryDataDirectory(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    public void Dispose()
    {
        var fixtureRoot = Directory.GetParent(_root)?.FullName;
        if (fixtureRoot is not null && Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private string DataDirectory => Path.Combine(_root, "data");

    private string CreateFile(string relativeDirectory, string fileName)
    {
        var directory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
