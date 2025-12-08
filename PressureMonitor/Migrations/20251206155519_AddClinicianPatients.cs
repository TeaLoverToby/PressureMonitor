using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PressureMonitor.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicianPatients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClinicianId",
                table: "Patients",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicianId",
                table: "Patients",
                column: "ClinicianId");

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Clinicians_ClinicianId",
                table: "Patients",
                column: "ClinicianId",
                principalTable: "Clinicians",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Clinicians_ClinicianId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicianId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ClinicianId",
                table: "Patients");
        }
    }
}
