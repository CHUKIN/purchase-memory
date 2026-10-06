using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptLineKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountedItemPosition",
                table: "receipt_capture_items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "receipt_capture_items",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Product");

            migrationBuilder.AddColumn<int>(
                name: "DiscountedItemPosition",
                table: "purchase_items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "purchase_items",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Product");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountedItemPosition",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "receipt_capture_items");

            migrationBuilder.DropColumn(
                name: "DiscountedItemPosition",
                table: "purchase_items");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "purchase_items");
        }
    }
}
