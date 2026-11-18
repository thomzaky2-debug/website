using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class InstaPaySenderHandle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstaPaySenderHandle",
                table: "Orders",
                type: "nvarchar(109)",
                maxLength: 109,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstaPaySenderHandle",
                table: "Orders");
        }
    }
}
