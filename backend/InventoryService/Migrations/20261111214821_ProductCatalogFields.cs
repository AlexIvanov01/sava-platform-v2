using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Migrations
{
    /// <inheritdoc />
    public partial class ProductCatalogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Products",
                type: "char(36)",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Products",
                type: "tinyint(1)",
                nullable: true
            );

            migrationBuilder.AddColumn<decimal>(
                name: "ListPrice",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SKU",
                table: "Products",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasure",
                table: "Products",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Products",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "ProductBatches",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            // Backfill existing products before the unique SKU index is created:
            // generated SKUs (P-000001, ... in creation order), the highest batch sell price
            // as the list price, the standard VAT rate, and pieces as the unit.
            migrationBuilder.Sql(
                @"UPDATE Products p
                  JOIN (SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) AS n
                        FROM Products WHERE SKU IS NULL) r ON r.Id = p.Id
                  SET p.SKU = CONCAT('P-', LPAD(r.n, 6, '0'));"
            );
            migrationBuilder.Sql(
                @"UPDATE Products p
                  SET p.ListPrice = (SELECT MAX(b.SellPrice) FROM ProductBatches b WHERE b.ProductId = p.Id)
                  WHERE p.ListPrice IS NULL;"
            );
            migrationBuilder.Sql(
                @"UPDATE Products
                  SET VatRate = COALESCE(VatRate, 20.00),
                      UnitOfMeasure = COALESCE(UnitOfMeasure, 'Piece'),
                      IsPublished = COALESCE(IsPublished, 0);"
            );

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
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products"
            );

            migrationBuilder.DropTable(name: "Categories");

            migrationBuilder.DropIndex(name: "IX_Products_CategoryId", table: "Products");

            migrationBuilder.DropIndex(name: "IX_Products_SKU", table: "Products");

            migrationBuilder.DropColumn(name: "CategoryId", table: "Products");

            migrationBuilder.DropColumn(name: "IsPublished", table: "Products");

            migrationBuilder.DropColumn(name: "ListPrice", table: "Products");

            migrationBuilder.DropColumn(name: "SKU", table: "Products");

            migrationBuilder.DropColumn(name: "UnitOfMeasure", table: "Products");

            migrationBuilder.DropColumn(name: "VatRate", table: "Products");

            migrationBuilder.DropColumn(name: "Location", table: "ProductBatches");
        }
    }
}
