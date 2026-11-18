using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNameSiToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameSi",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_NameSi",
                table: "Products",
                column: "NameSi");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_NameSi",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "NameSi",
                table: "Products");
        }
    }
}
