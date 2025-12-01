using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PressureMonitor.Migrations
{
    /// <inheritdoc />
    public partial class PressureMap_ToPatient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PressureMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientId = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PressureMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PressureMaps_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PressureFrames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PressureMapId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    AveragePressure = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactArea = table.Column<int>(type: "INTEGER", nullable: false),
                    PeakPressure = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PressureFrames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PressureFrames_PressureMaps_PressureMapId",
                        column: x => x.PressureMapId,
                        principalTable: "PressureMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PressureFrames_PressureMapId",
                table: "PressureFrames",
                column: "PressureMapId");

            migrationBuilder.CreateIndex(
                name: "IX_PressureMaps_PatientId",
                table: "PressureMaps",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PressureFrames");

            migrationBuilder.DropTable(
                name: "PressureMaps");
        }
    }
}
