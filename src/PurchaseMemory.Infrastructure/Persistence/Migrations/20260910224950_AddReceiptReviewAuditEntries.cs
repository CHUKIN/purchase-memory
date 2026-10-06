using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptReviewAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_review_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceiptCaptureId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_review_audit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_review_audit_entries_receipt_captures_ReceiptCaptureId",
                        column: x => x.ReceiptCaptureId,
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_review_audit_entries_ReceiptCaptureId_OccurredAt",
                table: "receipt_review_audit_entries",
                columns: new[] { "ReceiptCaptureId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_review_audit_entries");
        }
    }
}

#pragma warning restore CA1861
