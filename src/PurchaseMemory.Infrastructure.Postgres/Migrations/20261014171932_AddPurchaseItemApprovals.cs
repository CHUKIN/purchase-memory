using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseItemApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ApprovedAt",
                schema: "purchase_memory",
                table: "purchase_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByEmail",
                schema: "purchase_memory",
                table: "purchase_items",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                schema: "purchase_memory",
                table: "purchase_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "purchase_memory",
                table: "purchase_items");

            migrationBuilder.DropColumn(
                name: "ApprovedByEmail",
                schema: "purchase_memory",
                table: "purchase_items");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "purchase_memory",
                table: "purchase_items");
        }
    }
}
