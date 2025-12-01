using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PressureMonitor.Migrations
{
    /// <inheritdoc />
    public partial class StoreCalculatedMetricsInDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ContactArea",
                table: "PressureFrames",
                newName: "MinValue");

            migrationBuilder.AddColumn<int>(
                name: "AveragePressure",
                table: "PressureFrames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "ContactAreaPercentage",
                table: "PressureFrames",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "MaxValue",
                table: "PressureFrames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AveragePressure",
                table: "PressureFrames");

            migrationBuilder.DropColumn(
                name: "ContactAreaPercentage",
                table: "PressureFrames");

            migrationBuilder.DropColumn(
                name: "MaxValue",
                table: "PressureFrames");

            migrationBuilder.RenameColumn(
                name: "MinValue",
                table: "PressureFrames",
                newName: "ContactArea");
        }
    }
}
