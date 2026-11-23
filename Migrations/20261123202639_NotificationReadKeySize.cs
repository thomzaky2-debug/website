using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class NotificationReadKeySize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationReads",
                table: "NotificationReads");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "NotificationReads",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationReads",
                table: "NotificationReads",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationReads_NotificationId_UserId",
                table: "NotificationReads",
                columns: new[] { "NotificationId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationReads",
                table: "NotificationReads");

            migrationBuilder.DropIndex(
                name: "IX_NotificationReads_NotificationId_UserId",
                table: "NotificationReads");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "NotificationReads");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationReads",
                table: "NotificationReads",
                columns: new[] { "NotificationId", "UserId" });
        }
    }
}
