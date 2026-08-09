using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenamePriceTierQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MinQuantity",
                table: "ProductPriceTiers",
                newName: "Quantity");

            migrationBuilder.RenameIndex(
                name: "IX_ProductPriceTiers_ProductId_MinQuantity",
                table: "ProductPriceTiers",
                newName: "IX_ProductPriceTiers_ProductId_Quantity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "ProductPriceTiers",
                newName: "MinQuantity");

            migrationBuilder.RenameIndex(
                name: "IX_ProductPriceTiers_ProductId_Quantity",
                table: "ProductPriceTiers",
                newName: "IX_ProductPriceTiers_ProductId_MinQuantity");
        }
    }
}
