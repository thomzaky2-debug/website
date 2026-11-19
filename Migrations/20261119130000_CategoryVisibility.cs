using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NileShop.Data;
namespace NileShop.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119130000_CategoryVisibility")]
public class CategoryVisibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(name: "IsHidden", table: "Categories", type: "bit", nullable: false, defaultValue: false);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IsHidden", table: "Categories");
}
