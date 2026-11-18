using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NileShop.Migrations
{
    /// <inheritdoc />
    public partial class NileShopEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Colors",
                table: "Products",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GalleryImages",
                table: "Products",
                type: "nvarchar(max)",
                maxLength: 6000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sizes",
                table: "Products",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Carrier",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryMaxDays",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryMinDays",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "InventoryReleased",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsPaymentReview",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFee",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethodName",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingUrl",
                table: "Orders",
                type: "nvarchar(800)",
                maxLength: 800,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedColor",
                table: "OrderItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedSize",
                table: "OrderItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderEmails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderEmails_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderUpdates_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinDays = table.Column<int>(type: "int", nullable: false),
                    MaxDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                    table.CheckConstraint("CK_Shipping_Fee", "[Fee] >= 0 AND [MaxDays] >= [MinDays]");
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContactEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AddressEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddressAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnDays = table.Column<int>(type: "int", nullable: false),
                    DeliveryPolicyEn = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: true),
                    DeliveryPolicyAr = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: true),
                    ReturnsPolicyEn = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: true),
                    ReturnsPolicyAr = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderEmails_OrderId_Kind",
                table: "OrderEmails",
                columns: new[] { "OrderId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderEmails_SentAt_NextAttemptAt",
                table: "OrderEmails",
                columns: new[] { "SentAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderUpdates_OrderId",
                table: "OrderUpdates",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderEmails");

            migrationBuilder.DropTable(
                name: "OrderUpdates");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "Colors",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "GalleryImages",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Sizes",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Carrier",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryMaxDays",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryMinDays",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InventoryReleased",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NeedsPaymentReview",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrackingUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SelectedColor",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SelectedSize",
                table: "OrderItems");
        }
    }
}
