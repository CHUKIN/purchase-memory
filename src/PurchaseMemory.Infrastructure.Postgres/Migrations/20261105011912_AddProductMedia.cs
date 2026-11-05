using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AvoidSince",
                schema: "purchase_memory",
                table: "products",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_barcodes",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_barcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_barcodes_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_photos",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ThumbnailKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ThumbnailSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_photos_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "remembered_barcodes",
                schema: "purchase_memory",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    RememberedAt = table.Column<long>(type: "bigint", nullable: false),
                    RememberedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OffName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OffBrand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OffQuantity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OffImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remembered_barcodes", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_barcodes_Code",
                schema: "purchase_memory",
                table: "product_barcodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_barcodes_ProductId",
                schema: "purchase_memory",
                table: "product_barcodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_photos_ProductId_ClientId",
                schema: "purchase_memory",
                table: "product_photos",
                columns: new[] { "ProductId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_photos_ProductId_CreatedAt",
                schema: "purchase_memory",
                table: "product_photos",
                columns: new[] { "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_remembered_barcodes_RememberedAt",
                schema: "purchase_memory",
                table: "remembered_barcodes",
                column: "RememberedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_barcodes",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "product_photos",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "remembered_barcodes",
                schema: "purchase_memory");

            migrationBuilder.DropColumn(
                name: "AvoidSince",
                schema: "purchase_memory",
                table: "products");
        }
    }
}
