using System.Text;

namespace PurchaseMemory.ProductBackfill;

/// <summary>
/// Decides whether a database path is the household's live database. The backfill
/// tool migrates and rewrites whatever it is handed, so this is the last line
/// between a mistyped path and the owner's real purchase history.
/// </summary>
public static class ProductionDatabaseGuard
{
    public const string RepositoryMarkerFileName = "PurchaseMemory.slnx";

    private const string DataDirectoryName = "data";
    private const string ProductionDatabaseFileName = "purchases.db";

    /// <summary>
    /// True for any <c>data/purchases.db</c> — which is how both the repository
    /// and a deployed installation are laid out — and for anything at all inside
    /// <paramref name="repositoryDataDirectory"/>, which also covers
    /// <c>purchases.db-wal</c> and <c>data/backups/</c>.
    /// Both paths are link-resolved here, so a symlink cannot smuggle the live
    /// database past the check.
    /// </summary>
    public static bool IsProductionDatabase(string databasePath, string? repositoryDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var resolved = ResolveRealPath(databasePath);
        var directory = Path.GetDirectoryName(resolved);
        if (directory is null)
        {
            return false;
        }

        if (string.Equals(Path.GetFileName(resolved), ProductionDatabaseFileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(directory), DataDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (repositoryDataDirectory is null)
        {
            return false;
        }

        // macOS reports a runtime path in NFD while a typed argument arrives in
        // NFC, so a repository path containing non-ASCII letters («Учёт GPT»)
        // would never match a code-unit comparison. Both sides are folded first.
        var candidate = Comparable(directory) + Path.DirectorySeparatorChar;
        var root = Comparable(ResolveRealDirectory(repositoryDataDirectory)) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to the repository marker
    /// file and returns its <c>data</c> directory, or null when there is none.
    /// </summary>
    public static string? FindRepositoryDataDirectory(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        var current = new DirectoryInfo(ResolveRealDirectory(startDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, RepositoryMarkerFileName)))
            {
                var dataDirectory = Path.Combine(current.FullName, DataDirectoryName);
                return Directory.Exists(dataDirectory) ? ResolveRealDirectory(dataDirectory) : null;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Fully resolves a file path: the file itself may be a symlink, and so may
    /// any directory above it. <see cref="Path.GetFullPath(string)"/> only
    /// collapses «..», which is not enough on its own.
    /// </summary>
    public static string ResolveRealPath(string path)
    {
        try
        {
            var resolved = Path.GetFullPath(path);
            if (File.ResolveLinkTarget(resolved, returnFinalTarget: true) is { } target)
            {
                resolved = Path.GetFullPath(target.FullName);
            }

            var directory = Path.GetDirectoryName(resolved);
            return directory is null
                ? resolved
                : Path.Combine(ResolveRealDirectory(directory), Path.GetFileName(resolved));
        }
        catch (IOException)
        {
            // A broken or cyclic link cannot be resolved. The unresolved path is
            // still checked, so the guard stays fail-safe rather than silent.
            return Path.GetFullPath(path);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    public static string ResolveRealDirectory(string directory)
    {
        try
        {
            var resolved = Path.GetFullPath(directory);
            if (Directory.ResolveLinkTarget(resolved, returnFinalTarget: true) is { } target)
            {
                resolved = Path.GetFullPath(target.FullName);
            }

            var parent = Path.GetDirectoryName(resolved);
            return parent is null
                ? resolved
                : Path.Combine(ResolveRealDirectory(parent), Path.GetFileName(resolved));
        }
        catch (IOException)
        {
            return Path.GetFullPath(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(directory);
        }
    }

    private static string Comparable(string path)
    {
        return path.Normalize(NormalizationForm.FormC);
    }
}
