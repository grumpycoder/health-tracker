using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class Cessation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CessationGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Substance = table.Column<string>(type: "TEXT", nullable: false),
                    QuitDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Taper = table.Column<bool>(type: "INTEGER", nullable: false),
                    BaselineUnitsPerDay = table.Column<double>(type: "REAL", nullable: true),
                    CostPerUnit = table.Column<double>(type: "REAL", nullable: true),
                    UnitName = table.Column<string>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotificationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CessationGoals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CessationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GoalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Intensity = table.Column<int>(type: "INTEGER", nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", nullable: true),
                    Amount = table.Column<double>(type: "REAL", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CessationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CessationEvents_CessationGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "CessationGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CessationEvents_GoalId_Time",
                table: "CessationEvents",
                columns: new[] { "GoalId", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CessationEvents");

            migrationBuilder.DropTable(
                name: "CessationGoals");
        }
    }
}
