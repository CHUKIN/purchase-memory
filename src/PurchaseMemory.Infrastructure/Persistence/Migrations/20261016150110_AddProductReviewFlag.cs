using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductReviewFlag : Migration
    {
        /// <summary>
        /// A SQLite expression producing a fresh random GUID in the uppercase
        /// text form EF Core stores (version 4 layout). Evaluated per row.
        /// </summary>
        private const string NewGuidSql =
            "(upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-4' || "
            + "substr(upper(hex(randomblob(2))), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || "
            + "substr(upper(hex(randomblob(2))), 2) || '-' || upper(hex(randomblob(6))))";

        /// <summary>
        /// Every line that was waiting in a pending match becomes linked to a
        /// product. A store whose wording already has an alias simply joins
        /// that alias's product. The rest are grouped by wording alone (the
        /// alias normalization), across stores, as the matcher does at run
        /// time: one flagged product per wording, with an AutoCreated alias
        /// for every store it appeared in (aliases are keyed by store +
        /// wording, so these never collide). Candidates are the union over the
        /// group (best score per candidate, top five, never the product
        /// itself), and the flag is audited like the matcher does. Runs inside
        /// the migration's transaction, before the pending tables are dropped.
        /// </summary>
        private static string ConvertPendingMatchesSql() => $"""
            -- One row per store + wording still waiting for a product.
            CREATE TEMP TABLE review_fix (
                MerchantKey TEXT NOT NULL,
                NormalizedText TEXT NOT NULL,
                RawName TEXT NOT NULL,
                RaisedAt INTEGER NOT NULL,
                ProductId TEXT NULL,
                IsNew INTEGER NOT NULL DEFAULT 0
            );

            -- SQLite takes the bare RawItemName from the row holding MIN(RaisedAt).
            INSERT INTO review_fix (MerchantKey, NormalizedText, RawName, RaisedAt)
            SELECT m.MerchantKey, m.NormalizedItemName, m.RawItemName, MIN(m.RaisedAt)
            FROM pending_product_matches AS m
            JOIN purchase_items AS i ON i.Id = m.PurchaseItemId
            WHERE i.ProductId IS NULL AND i.Kind <> 'Discount'
            GROUP BY m.MerchantKey, m.NormalizedItemName;

            UPDATE review_fix
            SET ProductId = (
                SELECT a.ProductId FROM product_aliases AS a
                WHERE a.MerchantKey = review_fix.MerchantKey AND a.NormalizedText = review_fix.NormalizedText);

            -- One new flagged product per wording, named after its earliest line.
            CREATE TEMP TABLE review_new (
                NormalizedText TEXT NOT NULL PRIMARY KEY,
                RawName TEXT NOT NULL,
                RaisedAt INTEGER NOT NULL,
                ProductId TEXT NOT NULL
            );

            INSERT INTO review_new (NormalizedText, RawName, RaisedAt, ProductId)
            SELECT NormalizedText, RawName, MIN(RaisedAt), {NewGuidSql}
            FROM review_fix WHERE ProductId IS NULL
            GROUP BY NormalizedText;

            UPDATE review_fix
            SET ProductId = (SELECT n.ProductId FROM review_new AS n WHERE n.NormalizedText = review_fix.NormalizedText),
                IsNew = 1
            WHERE ProductId IS NULL;

            INSERT INTO products (Id, CanonicalName, NormalizedName, CreatedAt, UpdatedAt, Origin, NeedsReview, ReviewReason)
            SELECT ProductId, RawName, NormalizedText, RaisedAt, RaisedAt, 'Receipt', 1, 'AmbiguousMatch'
            FROM review_new;

            INSERT INTO product_aliases (Id, MerchantKey, NormalizedText, RawText, ProductId, Source, CreatedAt)
            SELECT {NewGuidSql}, MerchantKey, NormalizedText, RawName, ProductId, 'AutoCreated', RaisedAt
            FROM review_fix WHERE IsNew = 1;

            INSERT INTO product_audit_entries (Id, ProductId, Actor, ActorEmail, FieldName, Source, OldValue, NewValue, OccurredAt)
            SELECT {NewGuidSql}, ProductId, 'system', NULL, 'review', 'Matching', NULL, 'AmbiguousMatch', RaisedAt
            FROM review_new;

            INSERT INTO product_review_candidates (Id, ProductId, Position, CandidateProductId, Score)
            SELECT {NewGuidSql}, ranked.ProductId, ranked.Rank - 1, ranked.CandidateProductId, ranked.Score
            FROM (
                SELECT f.ProductId AS ProductId,
                       c.ProductId AS CandidateProductId,
                       MAX(c.Score) AS Score,
                       ROW_NUMBER() OVER (PARTITION BY f.ProductId ORDER BY MAX(c.Score) DESC, c.ProductId) AS Rank
                FROM review_fix AS f
                JOIN pending_product_matches AS m
                    ON m.MerchantKey = f.MerchantKey AND m.NormalizedItemName = f.NormalizedText
                JOIN purchase_items AS i ON i.Id = m.PurchaseItemId
                JOIN pending_product_match_candidates AS c ON c.PendingProductMatchId = m.Id
                WHERE f.IsNew = 1 AND c.ProductId <> f.ProductId
                    AND i.ProductId IS NULL AND i.Kind <> 'Discount'
                GROUP BY f.ProductId, c.ProductId
            ) AS ranked
            WHERE ranked.Rank <= 5;

            UPDATE purchase_items
            SET ProductId = (
                SELECT f.ProductId
                FROM pending_product_matches AS m
                JOIN review_fix AS f ON f.MerchantKey = m.MerchantKey AND f.NormalizedText = m.NormalizedItemName
                WHERE m.PurchaseItemId = purchase_items.Id)
            WHERE ProductId IS NULL
                AND Kind <> 'Discount'
                AND Id IN (SELECT PurchaseItemId FROM pending_product_matches);

            DROP TABLE review_new;
            DROP TABLE review_fix;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "products",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "products",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Receipt");

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "products",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_review_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_review_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_review_candidates_products_CandidateProductId",
                        column: x => x.CandidateProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_review_candidates_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_NeedsReview",
                table: "products",
                column: "NeedsReview");

            migrationBuilder.CreateIndex(
                name: "IX_product_review_candidates_CandidateProductId",
                table: "product_review_candidates",
                column: "CandidateProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_review_candidates_ProductId_Position",
                table: "product_review_candidates",
                columns: new[] { "ProductId", "Position" },
                unique: true);

            migrationBuilder.Sql(ConvertPendingMatchesSql());

            migrationBuilder.DropTable(
                name: "pending_product_match_candidates");

            migrationBuilder.DropTable(
                name: "pending_product_matches");
        }

        /// <summary>
        /// Schema only: the products created from pending matches stay (they
        /// are ordinary products then), and the pending tables come back empty.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_review_candidates");

            migrationBuilder.DropIndex(
                name: "IX_products_NeedsReview",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "products");

            migrationBuilder.CreateTable(
                name: "pending_product_matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MerchantKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedItemName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RaisedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RawItemName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_product_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_product_matches_purchase_items_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "purchase_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_product_match_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PendingProductMatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_product_match_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_product_match_candidates_pending_product_matches_PendingProductMatchId",
                        column: x => x.PendingProductMatchId,
                        principalTable: "pending_product_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pending_product_match_candidates_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_match_candidates_PendingProductMatchId_Position",
                table: "pending_product_match_candidates",
                columns: new[] { "PendingProductMatchId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_match_candidates_ProductId",
                table: "pending_product_match_candidates",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_matches_PurchaseItemId",
                table: "pending_product_matches",
                column: "PurchaseItemId",
                unique: true);
        }
    }
}

#pragma warning restore CA1861
