using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class NilePayPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentAccessToken",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unpaid");

            migrationBuilder.Sql("UPDATE [Orders] SET [PaymentAccessToken] = CONVERT(varchar(64), CRYPT_GEN_RANDOM(32), 2) WHERE [PaymentAccessToken] = N''");

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GatewayStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaymentAttemptId = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentAccessToken",
                table: "Orders",
                column: "PaymentAccessToken");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId",
                table: "PaymentAttempts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_SessionId",
                table: "PaymentAttempts",
                column: "SessionId",
                unique: true,
                filter: "[SessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_EventId",
                table: "PaymentEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentAttemptId",
                table: "PaymentEvents",
                column: "PaymentAttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "PaymentAttempts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentAccessToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAccessToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");
        }
    }
}
