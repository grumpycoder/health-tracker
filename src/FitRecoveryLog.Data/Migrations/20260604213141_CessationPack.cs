using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitRecoveryLog.Data.Migrations
{
    /// <inheritdoc />
    public partial class CessationPack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackName",
                table: "CessationGoals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "UnitsPerPack",
                table: "CessationGoals",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackName",
                table: "CessationGoals");

            migrationBuilder.DropColumn(
                name: "UnitsPerPack",
                table: "CessationGoals");
        }
    }
}
