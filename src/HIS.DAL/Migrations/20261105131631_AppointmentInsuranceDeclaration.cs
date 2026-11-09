using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HIS.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentInsuranceDeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_health_insurance",
                table: "appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_private_insurance",
                table: "appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_health_insurance",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "has_private_insurance",
                table: "appointments");
        }
    }
}
