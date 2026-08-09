using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class GoalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CoachingGoals = table.Column<string>(type: "TEXT", nullable: true),
                    IncludeCessationData = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProteinMin = table.Column<int>(type: "INTEGER", nullable: false),
                    ProteinMax = table.Column<int>(type: "INTEGER", nullable: false),
                    FatMin = table.Column<int>(type: "INTEGER", nullable: false),
                    FatMax = table.Column<int>(type: "INTEGER", nullable: false),
                    FiberMin = table.Column<int>(type: "INTEGER", nullable: false),
                    FiberMax = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedSugarMax = table.Column<int>(type: "INTEGER", nullable: false),
                    CaloriesRestMin = table.Column<int>(type: "INTEGER", nullable: false),
                    CaloriesRestMax = table.Column<int>(type: "INTEGER", nullable: false),
                    CaloriesActiveMin = table.Column<int>(type: "INTEGER", nullable: false),
                    CaloriesActiveMax = table.Column<int>(type: "INTEGER", nullable: false),
                    CarbsRestMin = table.Column<int>(type: "INTEGER", nullable: false),
                    CarbsRestMax = table.Column<int>(type: "INTEGER", nullable: false),
                    CarbsActiveMin = table.Column<int>(type: "INTEGER", nullable: false),
                    CarbsActiveMax = table.Column<int>(type: "INTEGER", nullable: false),
                    WaterRestMin = table.Column<int>(type: "INTEGER", nullable: false),
                    WaterRestMax = table.Column<int>(type: "INTEGER", nullable: false),
                    WaterActiveMin = table.Column<int>(type: "INTEGER", nullable: false),
                    WaterActiveMax = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalSettings");
        }
    }
}
