using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class ClientNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reconcile columns added by older deployments without duplicating them.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StoreSettings') AND name = 'CheckoutSessionLifetimeMinutes')
                BEGIN
                    ALTER TABLE StoreSettings ADD CheckoutSessionLifetimeMinutes INT NOT NULL CONSTRAINT DF_StoreSettings_Lifetime DEFAULT 15;
                END
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StoreSettings') AND name = 'FreeDeliveryOfferEnabled')
                BEGIN
                    ALTER TABLE StoreSettings ADD FreeDeliveryOfferEnabled BIT NOT NULL CONSTRAINT DF_StoreSettings_FreeDeliveryEnabled DEFAULT 0;
                END
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StoreSettings') AND name = 'FreeDeliveryOfferThreshold')
                BEGIN
                    ALTER TABLE StoreSettings ADD FreeDeliveryOfferThreshold DECIMAL(18,2) NOT NULL CONSTRAINT DF_StoreSettings_FreeDeliveryThreshold DEFAULT 0;
                END
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StoreSettings') AND name = 'FreeDeliveryOfferLabelEn')
                BEGIN
                    ALTER TABLE StoreSettings ADD FreeDeliveryOfferLabelEn NVARCHAR(200) NULL;
                END
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StoreSettings') AND name = 'FreeDeliveryOfferLabelAr')
                BEGIN
                    ALTER TABLE StoreSettings ADD FreeDeliveryOfferLabelAr NVARCHAR(200) NULL;
                END
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Order_Status' AND definition NOT LIKE '%Delivered%')
                BEGIN
                    ALTER TABLE [Orders] DROP CONSTRAINT [CK_Order_Status];
                    ALTER TABLE [Orders] ADD CONSTRAINT [CK_Order_Status] CHECK ([Status] IN ('Pending','Processing','Shipped','Delivered','Cancelled'));
                END
            """);

            migrationBuilder.CreateTable(
                name: "ClientNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SendKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientNotifications_AspNetUsers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationReads",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationReads", x => new { x.NotificationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_NotificationReads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationReads_ClientNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "ClientNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotifications_RecipientId_Id",
                table: "ClientNotifications",
                columns: new[] { "RecipientId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotifications_SendKey",
                table: "ClientNotifications",
                column: "SendKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationReads_UserId",
                table: "NotificationReads",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationReads");

            migrationBuilder.DropTable(
                name: "ClientNotifications");

            // Preserve existing delivery settings and Delivered orders on rollback.
        }
    }
}
