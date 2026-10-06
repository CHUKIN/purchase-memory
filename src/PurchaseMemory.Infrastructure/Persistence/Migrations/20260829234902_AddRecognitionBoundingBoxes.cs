using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecognitionBoundingBoxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ReceiptTotalBoundingBox_Height",
                table: "receipt_captures",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ReceiptTotalBoundingBox_Width",
                table: "receipt_captures",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ReceiptTotalBoundingBox_X",
                table: "receipt_captures",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ReceiptTotalBoundingBox_Y",
                table: "receipt_captures",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_Height",
                table: "receipt_capture_items",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_Width",
                table: "receipt_capture_items",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_X",
                table: "receipt_capture_items",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_Y",
                table: "receipt_capture_items",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptTotalBoundingBox_Height",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "ReceiptTotalBoundingBox_Width",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "ReceiptTotalBoundingBox_X",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "ReceiptTotalBoundingBox_Y",
                table: "receipt_captures");

            migrationBuilder.DropColumn(
                name: "BoundingBox_Height",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "BoundingBox_Width",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "BoundingBox_X",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "BoundingBox_Y",
                table: "receipt_capture_items");
        }
    }
}
