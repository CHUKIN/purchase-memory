using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImpressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_impressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AuthorEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Rating = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PhotoStorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PhotoContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_impressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_impressions_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_impressions_purchase_drafts_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "purchase_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_impressions_purchase_items_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "purchase_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_AuthorUserId_CreatedAt",
                table: "product_impressions",
                columns: new[] { "AuthorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_ProductId",
                table: "product_impressions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_PurchaseId_CreatedAt",
                table: "product_impressions",
                columns: new[] { "PurchaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_PurchaseItemId",
                table: "product_impressions",
                column: "PurchaseItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_impressions");
        }
    }
}
