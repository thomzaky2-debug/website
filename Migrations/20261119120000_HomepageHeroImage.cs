using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NileShop.Data;

namespace NileShop.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119120000_HomepageHeroImage")]
public class HomepageHeroImage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "HeroImageUrl", table: "StoreSettings", type: "nvarchar(500)", maxLength: 500, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "HeroImageUrl", table: "StoreSettings");
}
