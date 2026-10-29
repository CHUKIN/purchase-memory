using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NamePl",
                table: "products",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NamePlSource",
                table: "products",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameRu",
                table: "products",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameRuSource",
                table: "products",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NamingAttemptedAt",
                table: "products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedNamePl",
                table: "products",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedNameRu",
                table: "products",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NamePl",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NamePlSource",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NameRu",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NameRuSource",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NamingAttemptedAt",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NormalizedNamePl",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NormalizedNameRu",
                table: "products");
        }
    }
}
