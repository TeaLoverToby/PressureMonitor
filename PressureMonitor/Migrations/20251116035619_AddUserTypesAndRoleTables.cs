using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PressureMonitor.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTypesAndRoleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserType",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Clinicians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    LicenseNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Specialization = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HireDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinicians", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clinicians_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MedicalRecordNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BloodType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Allergies = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MedicalConditions = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Patients_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clinicians_UserId",
                table: "Clinicians",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_UserId",
                table: "Patients",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clinicians");

            migrationBuilder.DropTable(
                name: "Patients");

            migrationBuilder.DropColumn(
                name: "UserType",
                table: "Users");
        }
    }
}
