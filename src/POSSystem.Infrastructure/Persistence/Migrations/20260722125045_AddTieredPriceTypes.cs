using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTieredPriceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UnitPrice",
                table: "ProductPriceTiers",
                newName: "RetailPrice");

            migrationBuilder.AddColumn<decimal>(
                name: "SpecialPrice",
                table: "ProductPriceTiers",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WholesalePrice",
                table: "ProductPriceTiers",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecialPrice",
                table: "ProductPriceTiers");

            migrationBuilder.DropColumn(
                name: "WholesalePrice",
                table: "ProductPriceTiers");

            migrationBuilder.RenameColumn(
                name: "RetailPrice",
                table: "ProductPriceTiers",
                newName: "UnitPrice");
        }
    }
}
