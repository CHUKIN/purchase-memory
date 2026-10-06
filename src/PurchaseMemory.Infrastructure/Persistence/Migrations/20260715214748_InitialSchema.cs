using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceReceiptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MerchantName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PurchasedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: true),
                    ReceiptTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_drafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "receipt_captures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageStorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ImageContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ImageSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    MerchantName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PurchasedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: true),
                    ReceiptTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ExtractorName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ExtractorModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SavedPurchaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_captures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PurchaseDraftId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_items_purchase_drafts_PurchaseDraftId",
                        column: x => x.PurchaseDraftId,
                        principalTable: "purchase_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_capture_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ReceiptCaptureId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_capture_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_capture_items_receipt_captures_ReceiptCaptureId",
                        column: x => x.ReceiptCaptureId,
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_drafts_CreatedAt",
                table: "purchase_drafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_drafts_SourceReceiptId",
                table: "purchase_drafts",
                column: "SourceReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_items_PurchaseDraftId_Position",
                table: "purchase_items",
                columns: new[] { "PurchaseDraftId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_capture_items_ReceiptCaptureId_Position",
                table: "receipt_capture_items",
                columns: new[] { "ReceiptCaptureId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_CreatedAt",
                table: "receipt_captures",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_ImageSha256",
                table: "receipt_captures",
                column: "ImageSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_Status",
                table: "receipt_captures",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_items");

            migrationBuilder.DropTable(
                name: "receipt_capture_items");

            migrationBuilder.DropTable(
                name: "purchase_drafts");

            migrationBuilder.DropTable(
                name: "receipt_captures");
        }
    }
}

#pragma warning restore CA1861
