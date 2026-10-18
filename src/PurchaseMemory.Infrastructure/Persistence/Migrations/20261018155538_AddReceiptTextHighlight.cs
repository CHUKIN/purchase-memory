using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptTextHighlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TextLocationAttemptedAt",
                table: "receipt_captures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TextLocationAttempts",
                table: "receipt_captures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TextLocationCalls",
                table: "receipt_captures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TextLocationStatus",
                table: "receipt_captures",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequested");

            migrationBuilder.AddColumn<string>(
                name: "BoxSource",
                table: "receipt_capture_items",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Recognition");

            migrationBuilder.AddColumn<string>(
                name: "TextPolygons",
                table: "receipt_capture_items",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "receipt_text_layouts",
                columns: table => new
                {
                    ReceiptCaptureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PageWidth = table.Column<double>(type: "REAL", nullable: false),
                    PageHeight = table.Column<double>(type: "REAL", nullable: false),
                    Lines = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_text_layouts", x => x.ReceiptCaptureId);
                    table.ForeignKey(
                        name: "FK_receipt_text_layouts_receipt_captures_ReceiptCaptureId",
                        column: x => x.ReceiptCaptureId,
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_TextLocationStatus",
                table: "receipt_captures",
                column: "TextLocationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_text_layouts");

            migrationBuilder.DropIndex(
                name: "IX_receipt_captures_TextLocationStatus",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationAttemptedAt",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationAttempts",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationCalls",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationStatus",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "BoxSource",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "TextPolygons",
                table: "receipt_capture_items");
        }
    }
}
