using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Migrations
{
    /// <inheritdoc />
    public partial class AuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProductDateUpdated",
                table: "Products",
                newName: "UpdatedAt"
            );

            migrationBuilder.RenameColumn(
                name: "ProductDateAdded",
                table: "Products",
                newName: "CreatedAt"
            );

            migrationBuilder.RenameColumn(
                name: "BatchDateUpdated",
                table: "ProductBatches",
                newName: "UpdatedAt"
            );

            migrationBuilder.RenameColumn(
                name: "BatchDateAdded",
                table: "ProductBatches",
                newName: "CreatedAt"
            );

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Products",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Products",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "ProductBatches",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "ProductBatches",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CreatedBy", table: "Products");

            migrationBuilder.DropColumn(name: "UpdatedBy", table: "Products");

            migrationBuilder.DropColumn(name: "CreatedBy", table: "ProductBatches");

            migrationBuilder.DropColumn(name: "UpdatedBy", table: "ProductBatches");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "Products",
                newName: "ProductDateUpdated"
            );

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Products",
                newName: "ProductDateAdded"
            );

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "ProductBatches",
                newName: "BatchDateUpdated"
            );

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "ProductBatches",
                newName: "BatchDateAdded"
            );
        }
    }
}
