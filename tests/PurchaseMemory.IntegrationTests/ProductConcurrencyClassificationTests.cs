using Microsoft.EntityFrameworkCore;
using Npgsql;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Which failures of a merge or rename count as «changed at the same time»
/// (409) rather than a bug (500). The shapes are the ones Npgsql and EF
/// actually throw; the first is what two crossing merges produced on Postgres.
/// </summary>
public sealed class ProductConcurrencyClassificationTests
{
    private static PostgresException Deadlock() =>
        new("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);

    [Fact]
    public void DeadlockWrappedInTransientFailureAndSaveErrorIsAConflict()
    {
        var exception = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException("An error occurred while saving the entity changes.", Deadlock()));

        Assert.True(ProductMaintenanceRepository.IsConcurrentChange(exception));
    }

    [Fact]
    public void DatabaseErrorsAtAnyDepthAreConflicts()
    {
        Assert.True(ProductMaintenanceRepository.IsConcurrentChange(Deadlock()));
        Assert.True(ProductMaintenanceRepository.IsConcurrentChange(new DbUpdateException("save failed")));
        Assert.True(ProductMaintenanceRepository.IsConcurrentChange(
            new InvalidOperationException("transient failure", Deadlock())));
        Assert.True(ProductMaintenanceRepository.IsConcurrentChange(
            new InvalidOperationException("outer", new InvalidOperationException("transient failure", Deadlock()))));
    }

    [Fact]
    public void BugsAndOwnConflictsAreNotReclassified()
    {
        Assert.False(ProductMaintenanceRepository.IsConcurrentChange(new InvalidOperationException("a bug")));
        Assert.False(ProductMaintenanceRepository.IsConcurrentChange(
            new InvalidOperationException("a bug", new ArgumentException("bad argument"))));
        Assert.False(ProductMaintenanceRepository.IsConcurrentChange(new FormatException("a bug")));
        Assert.False(ProductMaintenanceRepository.IsConcurrentChange(
            new ProductConflictException("already a conflict", innerException: Deadlock())));
    }
}
