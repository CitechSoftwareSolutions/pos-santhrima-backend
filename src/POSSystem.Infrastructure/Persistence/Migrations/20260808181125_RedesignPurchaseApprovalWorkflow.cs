using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RedesignPurchaseApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Status",
                table: "Purchases",
                newName: "ApprovalStatus");

            migrationBuilder.RenameColumn(
                name: "ReceivedDate",
                table: "Purchases",
                newName: "StockAddedAt");

            migrationBuilder.RenameColumn(
                name: "QuantityReceived",
                table: "PurchaseItems",
                newName: "FreeQuantity");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAddedToStock",
                table: "Purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "Purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockAddedByUserId",
                table: "Purchases",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "IsAddedToStock",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "StockAddedByUserId",
                table: "Purchases");

            migrationBuilder.RenameColumn(
                name: "StockAddedAt",
                table: "Purchases",
                newName: "ReceivedDate");

            migrationBuilder.RenameColumn(
                name: "ApprovalStatus",
                table: "Purchases",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "FreeQuantity",
                table: "PurchaseItems",
                newName: "QuantityReceived");
        }
    }
}
