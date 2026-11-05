using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AvoidSince",
                table: "products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_barcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedByEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_barcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_barcodes_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ThumbnailKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ThumbnailSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsMain = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedByEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_photos_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "remembered_barcodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    RememberedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RememberedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OffName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OffBrand = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OffQuantity = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OffImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remembered_barcodes", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_barcodes_Code",
                table: "product_barcodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_barcodes_ProductId",
                table: "product_barcodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_photos_ProductId_ClientId",
                table: "product_photos",
                columns: new[] { "ProductId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_photos_ProductId_CreatedAt",
                table: "product_photos",
                columns: new[] { "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_remembered_barcodes_RememberedAt",
                table: "remembered_barcodes",
                column: "RememberedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_barcodes");

            migrationBuilder.DropTable(
                name: "product_photos");

            migrationBuilder.DropTable(
                name: "remembered_barcodes");

            migrationBuilder.DropColumn(
                name: "AvoidSince",
                table: "products");
        }
    }
}

#pragma warning restore CA1861
