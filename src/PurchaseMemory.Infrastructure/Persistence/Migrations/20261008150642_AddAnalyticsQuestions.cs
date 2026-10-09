using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF migrations generate constant column arrays.

namespace PurchaseMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedQuestion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsFollowUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlanJson = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodJson = table.Column<string>(type: "TEXT", nullable: true),
                    AnswerJson = table.Column<string>(type: "TEXT", nullable: false),
                    Conclusion = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AskedByUserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AskedByEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AnsweredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_questions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analytics_questions_AnsweredAt",
                table: "analytics_questions",
                column: "AnsweredAt");

            migrationBuilder.CreateIndex(
                name: "IX_analytics_questions_NormalizedQuestion_IsFollowUp",
                table: "analytics_questions",
                columns: new[] { "NormalizedQuestion", "IsFollowUp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_questions");
        }
    }
}
