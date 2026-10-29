using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NamePl",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NamePlSource",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameRu",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameRuSource",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NamingAttemptedAt",
                schema: "purchase_memory",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedNamePl",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedNameRu",
                schema: "purchase_memory",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NamePl",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NamePlSource",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NameRu",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NameRuSource",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NamingAttemptedAt",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NormalizedNamePl",
                schema: "purchase_memory",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NormalizedNameRu",
                schema: "purchase_memory",
                table: "products");
        }
    }
}
