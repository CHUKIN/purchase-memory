using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProductReviewFlag : Migration
    {
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
        /// Same steps as the SQLite migration, in Postgres dialect
        /// (gen_random_uuid is built in since 13).
        /// </summary>
        private const string ConvertPendingMatchesSql = """
            -- One row per store + wording still waiting for a product.
            CREATE TEMP TABLE review_fix (
                "MerchantKey" character varying(200) NOT NULL,
                "NormalizedText" character varying(500) NOT NULL,
                "RawName" character varying(500) NOT NULL,
                "RaisedAt" bigint NOT NULL,
                "ProductId" uuid NULL,
                "IsNew" boolean NOT NULL DEFAULT false
            );

            INSERT INTO review_fix ("MerchantKey", "NormalizedText", "RawName", "RaisedAt")
            SELECT DISTINCT ON (m."MerchantKey", m."NormalizedItemName")
                m."MerchantKey", m."NormalizedItemName", m."RawItemName", m."RaisedAt"
            FROM purchase_memory.pending_product_matches AS m
            JOIN purchase_memory.purchase_items AS i ON i."Id" = m."PurchaseItemId"
            WHERE i."ProductId" IS NULL AND i."Kind" <> 'Discount'
            ORDER BY m."MerchantKey", m."NormalizedItemName", m."RaisedAt", m."Id";

            UPDATE review_fix AS f
            SET "ProductId" = a."ProductId"
            FROM purchase_memory.product_aliases AS a
            WHERE a."MerchantKey" = f."MerchantKey" AND a."NormalizedText" = f."NormalizedText";

            -- One new flagged product per wording, named after its earliest line.
            CREATE TEMP TABLE review_new (
                "NormalizedText" character varying(500) NOT NULL PRIMARY KEY,
                "RawName" character varying(500) NOT NULL,
                "RaisedAt" bigint NOT NULL,
                "ProductId" uuid NOT NULL
            );

            INSERT INTO review_new ("NormalizedText", "RawName", "RaisedAt", "ProductId")
            SELECT grouped."NormalizedText", grouped."RawName", grouped."RaisedAt", gen_random_uuid()
            FROM (
                SELECT DISTINCT ON ("NormalizedText") "NormalizedText", "RawName", "RaisedAt"
                FROM review_fix
                WHERE "ProductId" IS NULL
                ORDER BY "NormalizedText", "RaisedAt", "MerchantKey"
            ) AS grouped;

            UPDATE review_fix AS f
            SET "ProductId" = n."ProductId", "IsNew" = true
            FROM review_new AS n
            WHERE n."NormalizedText" = f."NormalizedText" AND f."ProductId" IS NULL;

            INSERT INTO purchase_memory.products
                ("Id", "CanonicalName", "NormalizedName", "CreatedAt", "UpdatedAt", "Origin", "NeedsReview", "ReviewReason")
            SELECT "ProductId", "RawName", "NormalizedText", "RaisedAt", "RaisedAt", 'Receipt', true, 'AmbiguousMatch'
            FROM review_new;

            INSERT INTO purchase_memory.product_aliases
                ("Id", "MerchantKey", "NormalizedText", "RawText", "ProductId", "Source", "CreatedAt")
            SELECT gen_random_uuid(), "MerchantKey", "NormalizedText", "RawName", "ProductId", 'AutoCreated', "RaisedAt"
            FROM review_fix WHERE "IsNew";

            INSERT INTO purchase_memory.product_audit_entries
                ("Id", "ProductId", "Actor", "ActorEmail", "FieldName", "Source", "OldValue", "NewValue", "OccurredAt")
            SELECT gen_random_uuid(), "ProductId", 'system', NULL, 'review', 'Matching', NULL, 'AmbiguousMatch', "RaisedAt"
            FROM review_new;

            INSERT INTO purchase_memory.product_review_candidates ("Id", "ProductId", "Position", "CandidateProductId", "Score")
            SELECT gen_random_uuid(), ranked."ProductId", ranked."Rank" - 1, ranked."CandidateProductId", ranked."Score"
            FROM (
                SELECT f."ProductId" AS "ProductId",
                       c."ProductId" AS "CandidateProductId",
                       MAX(c."Score") AS "Score",
                       ROW_NUMBER() OVER (PARTITION BY f."ProductId" ORDER BY MAX(c."Score") DESC, c."ProductId") AS "Rank"
                FROM review_fix AS f
                JOIN purchase_memory.pending_product_matches AS m
                    ON m."MerchantKey" = f."MerchantKey" AND m."NormalizedItemName" = f."NormalizedText"
                JOIN purchase_memory.purchase_items AS i ON i."Id" = m."PurchaseItemId"
                JOIN purchase_memory.pending_product_match_candidates AS c ON c."PendingProductMatchId" = m."Id"
                WHERE f."IsNew" AND c."ProductId" <> f."ProductId"
                    AND i."ProductId" IS NULL AND i."Kind" <> 'Discount'
                GROUP BY f."ProductId", c."ProductId"
            ) AS ranked
            WHERE ranked."Rank" <= 5;

            UPDATE purchase_memory.purchase_items AS i
            SET "ProductId" = f."ProductId"
            FROM purchase_memory.pending_product_matches AS m
            JOIN review_fix AS f ON f."MerchantKey" = m."MerchantKey" AND f."NormalizedText" = m."NormalizedItemName"
            WHERE m."PurchaseItemId" = i."Id" AND i."ProductId" IS NULL AND i."Kind" <> 'Discount';

            DROP TABLE review_new;
            DROP TABLE review_fix;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                schema: "purchase_memory",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Receipt");

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_review_candidates",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CandidateProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_review_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_review_candidates_products_CandidateProductId",
                        column: x => x.CandidateProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_review_candidates_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_NeedsReview",
                schema: "purchase_memory",
                table: "products",
                column: "NeedsReview");

            migrationBuilder.CreateIndex(
                name: "IX_product_review_candidates_CandidateProductId",
                schema: "purchase_memory",
                table: "product_review_candidates",
                column: "CandidateProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_review_candidates_ProductId_Position",
                schema: "purchase_memory",
                table: "product_review_candidates",
                columns: new[] { "ProductId", "Position" },
                unique: true);

            migrationBuilder.Sql(ConvertPendingMatchesSql);

            migrationBuilder.DropTable(
                name: "pending_product_match_candidates",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "pending_product_matches",
                schema: "purchase_memory");
        }

        /// <summary>
        /// Schema only: the products created from pending matches stay (they
        /// are ordinary products then), and the pending tables come back empty.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_review_candidates",
                schema: "purchase_memory");

            migrationBuilder.DropIndex(
                name: "IX_products_NeedsReview",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.CreateTable(
                name: "pending_product_matches",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedItemName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaisedAt = table.Column<long>(type: "bigint", nullable: false),
                    RawItemName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_product_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_product_matches_purchase_items_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalSchema: "purchase_memory",
                        principalTable: "purchase_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_product_match_candidates",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PendingProductMatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_product_match_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_product_match_candidates_pending_product_matches_Pe~",
                        column: x => x.PendingProductMatchId,
                        principalSchema: "purchase_memory",
                        principalTable: "pending_product_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pending_product_match_candidates_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_match_candidates_PendingProductMatchId_Posi~",
                schema: "purchase_memory",
                table: "pending_product_match_candidates",
                columns: new[] { "PendingProductMatchId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_match_candidates_ProductId",
                schema: "purchase_memory",
                table: "pending_product_match_candidates",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_matches_PurchaseItemId",
                schema: "purchase_memory",
                table: "pending_product_matches",
                column: "PurchaseItemId",
                unique: true);
        }
    }
}
