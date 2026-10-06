using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategoriesAndBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "products",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandSource",
                table: "products",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CategorizationAttemptedAt",
                table: "products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorySource",
                table: "products",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedBrand",
                table: "products",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActorEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_audit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_audit_entries_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SystemKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_categories_product_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "product_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "product_categories",
                columns: new[] { "Id", "CreatedAt", "Name", "ParentId", "Position", "SystemKey", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-4000-8000-000000000001"), 1790380800000L, "Молочное и яйца", null, 0, "dairy", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000008"), 1790380800000L, "Мясо и рыба", null, 1, "meat_fish", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000013"), 1790380800000L, "Овощи и фрукты", null, 2, "produce", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000017"), 1790380800000L, "Хлеб и выпечка", null, 3, "bakery", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000020"), 1790380800000L, "Бакалея", null, 4, "grocery", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000028"), 1790380800000L, "Сладости и снеки", null, 5, "sweets_snacks", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000033"), 1790380800000L, "Напитки", null, 6, "drinks", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000040"), 1790380800000L, "Замороженное", null, 7, "frozen", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000044"), 1790380800000L, "Бытовая химия", null, 8, "household_chemicals", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000048"), 1790380800000L, "Гигиена", null, 9, "hygiene", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000052"), 1790380800000L, "Для дома", null, 10, "home", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000055"), 1790380800000L, "Прочее", null, 11, "other", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000002"), 1790380800000L, "Молоко", new Guid("00000000-0000-4000-8000-000000000001"), 0, "milk", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000003"), 1790380800000L, "Сыр", new Guid("00000000-0000-4000-8000-000000000001"), 1, "cheese", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000004"), 1790380800000L, "Йогурты и кефир", new Guid("00000000-0000-4000-8000-000000000001"), 2, "yogurt", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000005"), 1790380800000L, "Масло и сливки", new Guid("00000000-0000-4000-8000-000000000001"), 3, "butter_cream", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000006"), 1790380800000L, "Творог", new Guid("00000000-0000-4000-8000-000000000001"), 4, "curd", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000007"), 1790380800000L, "Яйца", new Guid("00000000-0000-4000-8000-000000000001"), 5, "eggs", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000009"), 1790380800000L, "Мясо", new Guid("00000000-0000-4000-8000-000000000008"), 0, "meat", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000010"), 1790380800000L, "Птица", new Guid("00000000-0000-4000-8000-000000000008"), 1, "poultry", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000011"), 1790380800000L, "Колбасы и сосиски", new Guid("00000000-0000-4000-8000-000000000008"), 2, "sausages", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000012"), 1790380800000L, "Рыба и морепродукты", new Guid("00000000-0000-4000-8000-000000000008"), 3, "fish", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000014"), 1790380800000L, "Овощи", new Guid("00000000-0000-4000-8000-000000000013"), 0, "vegetables", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000015"), 1790380800000L, "Фрукты", new Guid("00000000-0000-4000-8000-000000000013"), 1, "fruits", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000016"), 1790380800000L, "Зелень", new Guid("00000000-0000-4000-8000-000000000013"), 2, "herbs", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000018"), 1790380800000L, "Хлеб", new Guid("00000000-0000-4000-8000-000000000017"), 0, "bread", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000019"), 1790380800000L, "Выпечка", new Guid("00000000-0000-4000-8000-000000000017"), 1, "pastry", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000021"), 1790380800000L, "Макароны", new Guid("00000000-0000-4000-8000-000000000020"), 0, "pasta", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000022"), 1790380800000L, "Крупы и рис", new Guid("00000000-0000-4000-8000-000000000020"), 1, "grains", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000023"), 1790380800000L, "Мука и сахар", new Guid("00000000-0000-4000-8000-000000000020"), 2, "flour_sugar", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000024"), 1790380800000L, "Растительное масло", new Guid("00000000-0000-4000-8000-000000000020"), 3, "oils", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000025"), 1790380800000L, "Соусы и специи", new Guid("00000000-0000-4000-8000-000000000020"), 4, "sauces_spices", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000026"), 1790380800000L, "Консервы", new Guid("00000000-0000-4000-8000-000000000020"), 5, "canned", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000027"), 1790380800000L, "Хлопья и завтраки", new Guid("00000000-0000-4000-8000-000000000020"), 6, "breakfast", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000029"), 1790380800000L, "Шоколад и конфеты", new Guid("00000000-0000-4000-8000-000000000028"), 0, "chocolate", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000030"), 1790380800000L, "Печенье", new Guid("00000000-0000-4000-8000-000000000028"), 1, "cookies", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000031"), 1790380800000L, "Чипсы и снеки", new Guid("00000000-0000-4000-8000-000000000028"), 2, "chips", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000032"), 1790380800000L, "Орехи и сухофрукты", new Guid("00000000-0000-4000-8000-000000000028"), 3, "nuts", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000034"), 1790380800000L, "Кофе", new Guid("00000000-0000-4000-8000-000000000033"), 0, "coffee", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000035"), 1790380800000L, "Чай", new Guid("00000000-0000-4000-8000-000000000033"), 1, "tea", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000036"), 1790380800000L, "Соки", new Guid("00000000-0000-4000-8000-000000000033"), 2, "juices", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000037"), 1790380800000L, "Вода", new Guid("00000000-0000-4000-8000-000000000033"), 3, "water", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000038"), 1790380800000L, "Газировка", new Guid("00000000-0000-4000-8000-000000000033"), 4, "soft_drinks", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000039"), 1790380800000L, "Алкоголь", new Guid("00000000-0000-4000-8000-000000000033"), 5, "alcohol", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000041"), 1790380800000L, "Полуфабрикаты", new Guid("00000000-0000-4000-8000-000000000040"), 0, "frozen_meals", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000042"), 1790380800000L, "Мороженое", new Guid("00000000-0000-4000-8000-000000000040"), 1, "ice_cream", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000043"), 1790380800000L, "Замороженные овощи", new Guid("00000000-0000-4000-8000-000000000040"), 2, "frozen_vegetables", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000045"), 1790380800000L, "Стирка", new Guid("00000000-0000-4000-8000-000000000044"), 0, "laundry", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000046"), 1790380800000L, "Для посуды", new Guid("00000000-0000-4000-8000-000000000044"), 1, "dishwashing", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000047"), 1790380800000L, "Уборка", new Guid("00000000-0000-4000-8000-000000000044"), 2, "cleaning", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000049"), 1790380800000L, "Уход за телом", new Guid("00000000-0000-4000-8000-000000000048"), 0, "body_care", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000050"), 1790380800000L, "Уход за зубами", new Guid("00000000-0000-4000-8000-000000000048"), 1, "oral_care", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000051"), 1790380800000L, "Бумажные изделия", new Guid("00000000-0000-4000-8000-000000000048"), 2, "paper", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000053"), 1790380800000L, "Кухня", new Guid("00000000-0000-4000-8000-000000000052"), 0, "kitchen", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000054"), 1790380800000L, "Хозтовары", new Guid("00000000-0000-4000-8000-000000000052"), 1, "home_goods", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000056"), 1790380800000L, "Для животных", new Guid("00000000-0000-4000-8000-000000000055"), 0, "pets", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000057"), 1790380800000L, "Детское", new Guid("00000000-0000-4000-8000-000000000055"), 1, "baby", 1790380800000L },
                    { new Guid("00000000-0000-4000-8000-000000000058"), 1790380800000L, "Пакеты", new Guid("00000000-0000-4000-8000-000000000055"), 2, "bags", 1790380800000L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId",
                table: "products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_NormalizedBrand",
                table: "products",
                column: "NormalizedBrand");

            migrationBuilder.CreateIndex(
                name: "IX_product_audit_entries_ProductId_OccurredAt",
                table: "product_audit_entries",
                columns: new[] { "ProductId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_ParentId",
                table: "product_categories",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_products_product_categories_CategoryId",
                table: "products",
                column: "CategoryId",
                principalTable: "product_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_products_product_categories_CategoryId",
                table: "products");

            migrationBuilder.DropTable(
                name: "product_audit_entries");

            migrationBuilder.DropTable(
                name: "product_categories");

            migrationBuilder.DropIndex(
                name: "IX_products_CategoryId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_NormalizedBrand",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "products");

            migrationBuilder.DropColumn(
                name: "BrandSource",
                table: "products");

            migrationBuilder.DropColumn(
                name: "CategorizationAttemptedAt",
                table: "products");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "products");

            migrationBuilder.DropColumn(
                name: "CategorySource",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NormalizedBrand",
                table: "products");
        }
    }
}

#pragma warning restore CA1861
