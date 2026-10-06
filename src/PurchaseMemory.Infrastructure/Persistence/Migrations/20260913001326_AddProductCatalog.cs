using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "purchase_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pending_product_matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MerchantKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RawItemName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedItemName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RaisedAt = table.Column<long>(type: "INTEGER", nullable: false)
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
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanonicalName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pending_product_match_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    PendingProductMatchId = table.Column<Guid>(type: "TEXT", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "product_aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MerchantKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RawText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_aliases_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_items_ProductId",
                table: "purchase_items",
                column: "ProductId");

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

            migrationBuilder.CreateIndex(
                name: "IX_product_aliases_MerchantKey_NormalizedText",
                table: "product_aliases",
                columns: new[] { "MerchantKey", "NormalizedText" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_aliases_ProductId",
                table: "product_aliases",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_products_NormalizedName",
                table: "products",
                column: "NormalizedName");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_items_products_ProductId",
                table: "purchase_items",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_purchase_items_products_ProductId",
                table: "purchase_items");

            migrationBuilder.DropTable(
                name: "pending_product_match_candidates");

            migrationBuilder.DropTable(
                name: "product_aliases");

            migrationBuilder.DropTable(
                name: "pending_product_matches");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropIndex(
                name: "IX_purchase_items_ProductId",
                table: "purchase_items");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "purchase_items");
        }
    }
}

#pragma warning restore CA1861
