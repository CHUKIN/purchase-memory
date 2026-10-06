using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImpressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_impressions",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Rating = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PhotoStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PhotoContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    DeletedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_impressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_impressions_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_impressions_purchase_drafts_PurchaseId",
                        column: x => x.PurchaseId,
                        principalSchema: "purchase_memory",
                        principalTable: "purchase_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_impressions_purchase_items_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalSchema: "purchase_memory",
                        principalTable: "purchase_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_AuthorUserId_CreatedAt",
                schema: "purchase_memory",
                table: "product_impressions",
                columns: new[] { "AuthorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_ProductId",
                schema: "purchase_memory",
                table: "product_impressions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_PurchaseId_CreatedAt",
                schema: "purchase_memory",
                table: "product_impressions",
                columns: new[] { "PurchaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_impressions_PurchaseItemId",
                schema: "purchase_memory",
                table: "product_impressions",
                column: "PurchaseItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_impressions",
                schema: "purchase_memory");
        }
    }
}
