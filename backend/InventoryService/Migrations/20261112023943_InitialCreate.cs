using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Categories",
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
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        CreatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        UpdatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Categories", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

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
                        SKU = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        UnitOfMeasure = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: true),
                        ListPrice = table.Column<decimal>(
                            type: "decimal(18,2)",
                            precision: 18,
                            scale: 2,
                            nullable: true
                        ),
                        VatRate = table.Column<decimal>(
                            type: "decimal(5,2)",
                            precision: 5,
                            scale: 2,
                            nullable: true
                        ),
                        ReorderLevel = table.Column<int>(type: "int", nullable: true),
                        Weight = table.Column<float>(type: "float", nullable: true),
                        CategoryId = table.Column<Guid>(type: "char(36)", nullable: true),
                        HasOrders = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        CreatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        UpdatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Products", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Products_Categories_CategoryId",
                            column: x => x.CategoryId,
                            principalTable: "Categories",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict
                        );
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
                        ExpirationDate = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        Location = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        HasOrders = table.Column<bool>(type: "tinyint(1)", nullable: false),
                        ProductId = table.Column<Guid>(type: "char(36)", nullable: true),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        CreatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        UpdatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
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

            migrationBuilder
                .CreateTable(
                    name: "StockMovements",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        BatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                        Type = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: false
                        ),
                        Reason = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        Quantity = table.Column<int>(type: "int", nullable: false),
                        BalanceAfter = table.Column<int>(type: "int", nullable: false),
                        OrderId = table.Column<Guid>(type: "char(36)", nullable: true),
                        Note = table.Column<string>(
                            type: "varchar(500)",
                            maxLength: 500,
                            nullable: true
                        ),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        CreatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        UpdatedBy = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_StockMovements", x => x.Id);
                        table.ForeignKey(
                            name: "FK_StockMovements_ProductBatches_BatchId",
                            column: x => x.BatchId,
                            principalTable: "ProductBatches",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId",
                table: "ProductBatches",
                column: "ProductId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Products_SKU",
                table: "Products",
                column: "SKU",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BatchId",
                table: "StockMovements",
                column: "BatchId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrderId_BatchId",
                table: "StockMovements",
                columns: new[] { "OrderId", "BatchId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StockMovements");

            migrationBuilder.DropTable(name: "ProductBatches");

            migrationBuilder.DropTable(name: "Products");

            migrationBuilder.DropTable(name: "Categories");
        }
    }
}
