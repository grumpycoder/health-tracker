using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Server.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoachingGoals = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IncludeCessationData = table.Column<bool>(type: "bit", nullable: false),
                    ProteinMin = table.Column<int>(type: "int", nullable: false),
                    ProteinMax = table.Column<int>(type: "int", nullable: false),
                    FatMin = table.Column<int>(type: "int", nullable: false),
                    FatMax = table.Column<int>(type: "int", nullable: false),
                    FiberMin = table.Column<int>(type: "int", nullable: false),
                    FiberMax = table.Column<int>(type: "int", nullable: false),
                    AddedSugarMax = table.Column<int>(type: "int", nullable: false),
                    CaloriesRestMin = table.Column<int>(type: "int", nullable: false),
                    CaloriesRestMax = table.Column<int>(type: "int", nullable: false),
                    CaloriesActiveMin = table.Column<int>(type: "int", nullable: false),
                    CaloriesActiveMax = table.Column<int>(type: "int", nullable: false),
                    CarbsRestMin = table.Column<int>(type: "int", nullable: false),
                    CarbsRestMax = table.Column<int>(type: "int", nullable: false),
                    CarbsActiveMin = table.Column<int>(type: "int", nullable: false),
                    CarbsActiveMax = table.Column<int>(type: "int", nullable: false),
                    WaterRestMin = table.Column<int>(type: "int", nullable: false),
                    WaterRestMax = table.Column<int>(type: "int", nullable: false),
                    WaterActiveMin = table.Column<int>(type: "int", nullable: false),
                    WaterActiveMax = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
