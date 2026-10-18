using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptTextHighlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TextLocationAttemptedAt",
                schema: "purchase_memory",
                table: "receipt_captures",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TextLocationAttempts",
                schema: "purchase_memory",
                table: "receipt_captures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TextLocationCalls",
                schema: "purchase_memory",
                table: "receipt_captures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TextLocationStatus",
                schema: "purchase_memory",
                table: "receipt_captures",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequested");

            migrationBuilder.AddColumn<string>(
                name: "BoxSource",
                schema: "purchase_memory",
                table: "receipt_capture_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Recognition");

            migrationBuilder.AddColumn<string>(
                name: "TextPolygons",
                schema: "purchase_memory",
                table: "receipt_capture_items",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "receipt_text_layouts",
                schema: "purchase_memory",
                columns: table => new
                {
                    ReceiptCaptureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PageWidth = table.Column<double>(type: "double precision", nullable: false),
                    PageHeight = table.Column<double>(type: "double precision", nullable: false),
                    Lines = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_text_layouts", x => x.ReceiptCaptureId);
                    table.ForeignKey(
                        name: "FK_receipt_text_layouts_receipt_captures_ReceiptCaptureId",
                        column: x => x.ReceiptCaptureId,
                        principalSchema: "purchase_memory",
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_TextLocationStatus",
                schema: "purchase_memory",
                table: "receipt_captures",
                column: "TextLocationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_text_layouts",
                schema: "purchase_memory");

            migrationBuilder.DropIndex(
                name: "IX_receipt_captures_TextLocationStatus",
                schema: "purchase_memory",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationAttemptedAt",
                schema: "purchase_memory",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationAttempts",
                schema: "purchase_memory",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationCalls",
                schema: "purchase_memory",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "TextLocationStatus",
                schema: "purchase_memory",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "BoxSource",
                schema: "purchase_memory",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "TextPolygons",
                schema: "purchase_memory",
                table: "receipt_capture_items");
        }
    }
}
