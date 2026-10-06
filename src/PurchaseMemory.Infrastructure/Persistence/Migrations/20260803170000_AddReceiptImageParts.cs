using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PurchaseMemoryDbContext))]
[Migration("20260803170000_AddReceiptImageParts")]
public sealed class AddReceiptImageParts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "receipt_image_parts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Position = table.Column<int>(type: "INTEGER", nullable: false),
                StorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ReceiptCaptureId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipt_image_parts", x => x.Id);
                table.ForeignKey(
                    name: "FK_receipt_image_parts_receipt_captures_ReceiptCaptureId",
                    column: x => x.ReceiptCaptureId,
                    principalTable: "receipt_captures",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO receipt_image_parts
                (Id, Position, StorageKey, ContentType, SizeBytes, Sha256, ReceiptCaptureId)
            SELECT Id, 0, ImageStorageKey, ImageContentType, ImageSizeBytes, ImageSha256, Id
            FROM receipt_captures;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_receipt_image_parts_ReceiptCaptureId_Position",
            table: "receipt_image_parts",
            columns: ["ReceiptCaptureId", "Position"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "receipt_image_parts");
    }
}
