using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The swipe review's «checked» marks on purchase lines: the batch endpoint,
/// access, validation, idempotency, the detail DTO, marks surviving a
/// correction, and the additive migration.
/// </summary>
public sealed class PurchaseLineApprovalTests
{
    private const string AnnaId = "11111111-1111-1111-1111-111111111111";
    private const string BorisId = "22222222-2222-2222-2222-222222222222";
    private const string Url = "/api/purchases/line-approvals";

    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Anna => AuthTestHost.Token(subject: AnnaId, email: "anna@example.com");

    private static string Boris => AuthTestHost.Token(subject: BorisId, email: "boris@example.com");

    [Fact]
    public async Task ApprovingLinesShowsInTheDetailForEveryoneAndDoesNotTouchTheEditVersion()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m), ("MASŁO", 7.99m));
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var before = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        var lines = before.GetProperty("items").EnumerateArray().ToList();
        Assert.All(lines, line => Assert.False(line.GetProperty("approved").GetBoolean()));
        Assert.All(lines, line => Assert.Equal(JsonValueKind.Null, line.GetProperty("approvedAt").ValueKind));
        var swipedAt = DateTimeOffset.UtcNow.AddMinutes(-3);

        var response = await anna.PostAsJsonAsync(Url, Batch((purchase.PurchaseId, LineId(lines[0]), true, swipedAt)), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("items").EnumerateArray());
        Assert.Equal("applied", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("approved").GetBoolean());
        var answeredAt = result.GetProperty("approvedAt").GetDateTimeOffset();
        Assert.Equal(swipedAt.ToUnixTimeMilliseconds(), answeredAt.ToUnixTimeMilliseconds());

        // The other household member sees the mark, with who and when.
        var after = await boris.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        var approved = after.GetProperty("items")[0];
        Assert.True(approved.GetProperty("approved").GetBoolean());
        // The first answer and a reload agree to the millisecond (storage keeps whole ms).
        Assert.Equal(answeredAt, approved.GetProperty("approvedAt").GetDateTimeOffset());
        Assert.Equal("anna@example.com", approved.GetProperty("approvedByEmail").GetString());
        Assert.False(after.GetProperty("items")[1].GetProperty("approved").GetBoolean());
        // A mark is not an edit: an editor opened before it still saves without a conflict.
        Assert.Equal(before.GetProperty("updatedAt").GetDateTimeOffset(), after.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task RepeatingABatchChangesNothingAndTheFirstApprovalWins()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m));
        var lineId = purchase.LineIds.Single();
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var body = Batch((purchase.PurchaseId, lineId, true, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal("applied", (await PostAsync(anna, body))[0].GetProperty("status").GetString());
        Assert.Equal("unchanged", (await PostAsync(anna, body))[0].GetProperty("status").GetString());
        Assert.Equal("unchanged", (await PostAsync(boris, Batch((purchase.PurchaseId, lineId, true, null))))[0].GetProperty("status").GetString());

        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        Assert.Equal("anna@example.com", detail.GetProperty("items")[0].GetProperty("approvedByEmail").GetString());
    }

    [Fact]
    public async Task UndoAppliesOnlyWhenNewerThanTheApproval()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m));
        var lineId = purchase.LineIds.Single();
        using var anna = host.CreateClientFor(Anna);
        var approvedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await PostAsync(anna, Batch((purchase.PurchaseId, lineId, true, approvedAt)));

        var stale = await PostAsync(anna, Batch((purchase.PurchaseId, lineId, false, approvedAt.AddMinutes(-1))));
        Assert.Equal("unchanged", stale[0].GetProperty("status").GetString());
        Assert.True(stale[0].GetProperty("approved").GetBoolean());

        var undo = await PostAsync(anna, Batch((purchase.PurchaseId, lineId, false, approvedAt.AddMinutes(1))));
        Assert.Equal("applied", undo[0].GetProperty("status").GetString());
        Assert.False(undo[0].GetProperty("approved").GetBoolean());
        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        Assert.False(detail.GetProperty("items")[0].GetProperty("approved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("items")[0].GetProperty("approvedByEmail").ValueKind);
    }

    [Fact]
    public async Task InsideOneBatchTheNewestSwipeOfALineWinsAndTheClockIsClamped()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m), ("MASŁO", 7.99m), ("JAJA", 9.99m));
        using var anna = host.CreateClientFor(Anna);
        var now = DateTimeOffset.UtcNow;
        var first = purchase.LineIds[0];
        var second = purchase.LineIds[1];

        var results = await PostAsync(anna, Batch(
            (purchase.PurchaseId, first, false, now.AddSeconds(-5)),
            (purchase.PurchaseId, second, true, now.AddDays(3)),
            (purchase.PurchaseId, first, true, now.AddSeconds(-10)),
            (purchase.PurchaseId, purchase.LineIds[2], true, now.AddDays(-90))));

        Assert.Equal(3, results.Count);
        // A clock far behind is kept within the last 30 days.
        var old = results.Single(item => item.GetProperty("lineId").GetGuid() == purchase.LineIds[2]).GetProperty("approvedAt").GetDateTimeOffset();
        Assert.True(old >= DateTimeOffset.UtcNow.AddDays(-30).AddMinutes(-1) && old <= DateTimeOffset.UtcNow.AddDays(-29));
        Assert.False(results.Single(item => item.GetProperty("lineId").GetGuid() == first).GetProperty("approved").GetBoolean());
        var future = results.Single(item => item.GetProperty("lineId").GetGuid() == second);
        Assert.True(future.GetProperty("approved").GetBoolean());
        Assert.True(future.GetProperty("approvedAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task UnknownPurchasesAndLinesOfAnotherPurchaseAreNotFoundAndChangeNothing()
    {
        await using var host = new AuthTestHost();
        var mine = await SaveAsync(host.Services, ("CHLEB", 4.00m));
        var other = await SaveAsync(host.Services, ("KAWA", 15.00m));
        using var anna = host.CreateClientFor(Anna);

        var results = await PostAsync(anna, Batch(
            (Guid.NewGuid(), Guid.NewGuid(), true, null),
            // A real line, but claimed under the wrong purchase.
            (mine.PurchaseId, other.LineIds.Single(), true, null),
            (mine.PurchaseId, mine.LineIds.Single(), true, null)));

        Assert.Equal(["notFound", "notFound", "applied"], results.Select(item => item.GetProperty("status").GetString()));
        Assert.All(results.Take(2), item => Assert.False(item.GetProperty("approved").GetBoolean()));
        var otherDetail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{other.PurchaseId}", Cancel);
        Assert.False(otherDetail.GetProperty("items")[0].GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task OnlyApprovedUsersCanSendMarks()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m));
        var body = Batch((purchase.PurchaseId, purchase.LineIds.Single(), true, null));
        var callers = new (string? Token, HttpStatusCode Expected)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(key: AuthTestHost.ForeignKey), HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5)), HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(access: "pending", subject: BorisId), HttpStatusCode.Forbidden),
            (AuthTestHost.Token(access: "blocked", subject: BorisId), HttpStatusCode.Forbidden)
        };
        foreach (var (token, expected) in callers)
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(expected, (await client.PostAsJsonAsync(Url, body, Cancel)).StatusCode);
        }

        using var anna = host.CreateClientFor(Anna);
        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        Assert.False(detail.GetProperty("items")[0].GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task MalformedOrOversizedBatchesAreRejected()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m));
        var lineId = purchase.LineIds.Single();
        using var anna = host.CreateClientFor(Anna);

        await AssertBadRequestAsync(anna, new JsonObject { ["items"] = new JsonArray() }, "at least one");
        await AssertBadRequestAsync(anna, new JsonObject(), "at least one");
        await AssertBadRequestAsync(anna, new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["purchaseId"] = purchase.PurchaseId, ["lineId"] = lineId })
        }, "approved are required");
        await AssertBadRequestAsync(anna, Batch((Guid.Empty, lineId, true, null)), "required");
        await AssertBadRequestAsync(
            anna,
            Batch(Enumerable.Range(0, PurchaseLineApprovalService.MaximumChanges + 1)
                .Select(_ => (purchase.PurchaseId, Guid.NewGuid(), true, (DateTimeOffset?)null)).ToArray()),
            "at most");
        await AssertBadRequestAsync(
            anna,
            Batch(Enumerable.Range(0, PurchaseLineApprovalService.MaximumPurchases + 1)
                .Select(_ => (Guid.NewGuid(), Guid.NewGuid(), true, (DateTimeOffset?)null)).ToArray()),
            "purchases");

        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        Assert.False(detail.GetProperty("items")[0].GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task ACorrectionKeepsTheMarkOfLinesItContinuesAndOnlyThose()
    {
        await using var host = new AuthTestHost();
        var purchase = await SaveAsync(host.Services, ("CHLEB", 4.00m), ("MASŁO", 7.99m), ("JAJA", 9.99m));
        using var anna = host.CreateClientFor(Anna);
        await PostAsync(anna, Batch(
            (purchase.PurchaseId, purchase.LineIds[0], true, null),
            (purchase.PurchaseId, purchase.LineIds[1], true, null)));
        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchase.PurchaseId}", Cancel);
        var items = detail.GetProperty("items").EnumerateArray().ToList();

        // Line 1 is corrected (keeps its id), line 2 is sent without an id
        // (an older client), line 3 stays, and a new line is added.
        var response = await anna.PutAsJsonAsync($"/api/purchases/{purchase.PurchaseId}", new
        {
            expectedUpdatedAt = detail.GetProperty("updatedAt").GetString(),
            merchantName = "Lidl",
            purchasedAt = July1,
            currency = "PLN",
            receiptTotal = (decimal?)null,
            items = new object[]
            {
                new { id = LineId(items[0]), name = "CHLEB ŻYTNI", quantity = 1, unitPrice = 4.50m, lineTotal = 4.50m },
                new { name = "MASŁO", quantity = 1, unitPrice = 7.99m, lineTotal = 7.99m },
                new { id = LineId(items[2]), name = "JAJA", quantity = 1, unitPrice = 9.99m, lineTotal = 9.99m },
                new { name = "MLEKO", quantity = 1, unitPrice = 3.49m, lineTotal = 3.49m }
            }
        }, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([true, false, false, false], updated.Select(item => item.GetProperty("approved").GetBoolean()));
        Assert.NotEqual(LineId(items[0]), LineId(updated[0]));
        Assert.Equal(
            items[0].GetProperty("approvedAt").GetDateTimeOffset(),
            updated[0].GetProperty("approvedAt").GetDateTimeOffset());
        Assert.Equal("anna@example.com", updated[0].GetProperty("approvedByEmail").GetString());

        // A mark sent for a line the correction replaced is reported, not applied.
        var stale = await PostAsync(anna, Batch((purchase.PurchaseId, LineId(items[2]), true, null)));
        Assert.Equal("notFound", stale[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnExistingDatabaseGainsUncheckedLines()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "migration.db")};Pooling=False")
                .Options;
            await using (var oldContext = new PurchaseMemoryDbContext(options))
            {
                await oldContext.Database.MigrateAsync("20261008150642_AddAnalyticsQuestions", Cancel);
                var purchaseId = Guid.NewGuid();
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await oldContext.Database.ExecuteSqlAsync(
                    $"INSERT INTO purchase_drafts (Id, CreatedAt, UpdatedAt) VALUES ({purchaseId.ToString().ToUpperInvariant()}, {now}, {now})",
                    Cancel);
                await oldContext.Database.ExecuteSqlAsync(
                    $"INSERT INTO purchase_items (Id, PurchaseDraftId, Position, Name, Kind) VALUES ({Guid.NewGuid().ToString().ToUpperInvariant()}, {purchaseId.ToString().ToUpperInvariant()}, 0, {"CHLEB"}, {"Product"})",
                    Cancel);
            }

            await using var upgraded = new PurchaseMemoryDbContext(options);
            await upgraded.Database.MigrateAsync(Cancel);
            var draft = await upgraded.PurchaseDrafts.Include(purchase => purchase.Items).SingleAsync(Cancel);
            var item = draft.Items.Single();
            Assert.Equal("CHLEB", item.Name);
            Assert.False(item.IsApproved);
            Assert.True(item.Approve(AnnaId, "anna@example.com", DateTimeOffset.UtcNow));
            await upgraded.SaveChangesAsync(Cancel);
            Assert.False(upgraded.Database.HasPendingModelChanges());

            // The migration rolls back cleanly as well, keeping the line.
            await upgraded.Database.MigrateAsync("20261008150642_AddAnalyticsQuestions", Cancel);
            Assert.DoesNotContain("20261014171920_AddPurchaseItemApprovals", await upgraded.Database.GetAppliedMigrationsAsync(Cancel));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Guid LineId(JsonElement item) => item.GetProperty("id").GetGuid();

    private static JsonObject Batch(params (Guid PurchaseId, Guid LineId, bool Approved, DateTimeOffset? At)[] entries)
    {
        var items = new JsonArray();
        foreach (var (purchaseId, lineId, approved, at) in entries)
        {
            items.Add(new JsonObject
            {
                ["purchaseId"] = purchaseId,
                ["lineId"] = lineId,
                ["approved"] = approved,
                ["at"] = at
            });
        }

        return new JsonObject { ["items"] = items };
    }

    private static async Task<List<JsonElement>> PostAsync(HttpClient client, JsonObject body)
    {
        var response = await client.PostAsJsonAsync(Url, body, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task AssertBadRequestAsync(HttpClient client, JsonObject body, string expectedText)
    {
        var response = await client.PostAsJsonAsync(Url, body, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var detail = problem.TryGetProperty("detail", out var value) ? value.GetString() ?? string.Empty : problem.ToString();
        Assert.Contains(expectedText, detail, StringComparison.OrdinalIgnoreCase);
        // Plain words for the client, no framework internals.
        Assert.DoesNotContain("Parameter", detail, StringComparison.Ordinal);
    }

    private sealed record Saved(Guid PurchaseId, List<Guid> LineIds);

    private static async Task<Saved> SaveAsync(IServiceProvider services, params (string Name, decimal Price)[] items)
    {
        await using var scope = services.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                "Lidl",
                July1,
                "PLN",
                null,
                items.Select(item => new PurchaseItemCommand(item.Name, 1, "szt", item.Price, item.Price)).ToList(),
                AllowPossibleDuplicate: true),
            Cancel);
        var lineIds = await scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>().PurchaseDrafts
            .Where(purchase => purchase.Id == view.PurchaseId)
            .SelectMany(purchase => purchase.Items)
            .OrderBy(item => item.Position)
            .Select(item => item.Id)
            .ToListAsync(Cancel);
        return new Saved(view.PurchaseId, lineIds);
    }
}
