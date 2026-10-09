using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_questions",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedQuestion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsFollowUp = table.Column<bool>(type: "boolean", nullable: false),
                    PlanJson = table.Column<string>(type: "text", nullable: false),
                    PeriodJson = table.Column<string>(type: "text", nullable: true),
                    AnswerJson = table.Column<string>(type: "text", nullable: false),
                    Conclusion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AskedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AskedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    AnsweredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_questions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analytics_questions_AnsweredAt",
                schema: "purchase_memory",
                table: "analytics_questions",
                column: "AnsweredAt");

            migrationBuilder.CreateIndex(
                name: "IX_analytics_questions_NormalizedQuestion_IsFollowUp",
                schema: "purchase_memory",
                table: "analytics_questions",
                columns: new[] { "NormalizedQuestion", "IsFollowUp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_questions",
                schema: "purchase_memory");
        }
    }
}
