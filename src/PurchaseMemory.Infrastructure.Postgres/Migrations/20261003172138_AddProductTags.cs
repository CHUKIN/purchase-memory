using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_tags",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_tag_links",
                schema: "purchase_memory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_tag_links", x => new { x.ProductId, x.TagId });
                    table.ForeignKey(
                        name: "FK_product_tag_links_product_tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "purchase_memory",
                        principalTable: "product_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_tag_links_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_tag_links_TagId",
                schema: "purchase_memory",
                table: "product_tag_links",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_product_tags_NormalizedName",
                schema: "purchase_memory",
                table: "product_tags",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_tag_links",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "product_tags",
                schema: "purchase_memory");
        }
    }
}
