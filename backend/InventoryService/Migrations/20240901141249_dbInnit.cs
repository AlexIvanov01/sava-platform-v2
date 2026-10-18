using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Migrations
{
    /// <inheritdoc />
    public partial class dbInnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Products",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        Name = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Description = table.Column<string>(
                            type: "varchar(500)",
                            maxLength: 500,
                            nullable: true
                        ),
                        Brand = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        Supplier = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        ImageURL = table.Column<string>(
                            type: "varchar(200)",
                            maxLength: 200,
                            nullable: true
                        ),
                        Status = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        Barcode = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        ReorderLevel = table.Column<int>(type: "int", nullable: true),
                        Weight = table.Column<float>(type: "float", nullable: true),
                        ProductDateAdded = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        ProductDateUpdated = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Products", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "ProductBatches",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        Lot = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        Quantity = table.Column<int>(type: "int", nullable: true),
                        PurchasePrice = table.Column<decimal>(
                            type: "decimal(18,2)",
                            nullable: true
                        ),
                        SellPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                        BatchDateAdded = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        BatchDateUpdated = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        ExpirationDate = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        ProductId = table.Column<Guid>(type: "char(36)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_ProductBatches", x => x.Id);
                        table.ForeignKey(
                            name: "FK_ProductBatches_Products_ProductId",
                            column: x => x.ProductId,
                            principalTable: "Products",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId",
                table: "ProductBatches",
                column: "ProductId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProductBatches");

            migrationBuilder.DropTable(name: "Products");
        }
    }
}
