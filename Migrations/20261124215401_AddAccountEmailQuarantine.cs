using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountEmailQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Quarantined",
                table: "AccountEmailQueues",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quarantined",
                table: "AccountEmailQueues");
        }
    }
}
