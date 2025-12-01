using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PressureMonitor.Migrations
{
    /// <inheritdoc />
    public partial class Update2DArrayForPressureFrame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AveragePressure",
                table: "PressureFrames");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AveragePressure",
                table: "PressureFrames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
