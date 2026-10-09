using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DKNet.StaticData.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderOwnedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnedBy",
                schema: "manual_sample",
                table: "PurchaseOrders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnedBy",
                schema: "manual_sample",
                table: "PurchaseOrders");
        }
    }
}
