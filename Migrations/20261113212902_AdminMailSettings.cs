using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class AdminMailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreMailSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    From = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    EnableSsl = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreMailSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreMailSettings");
        }
    }
}
