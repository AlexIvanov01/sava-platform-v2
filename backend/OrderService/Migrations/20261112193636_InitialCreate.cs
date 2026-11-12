using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace OrderService.Migrations
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
                    name: "CompanySettings",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        Name = table.Column<string>(
                            type: "varchar(200)",
                            maxLength: 200,
                            nullable: true
                        ),
                        UIC = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        VATNumber = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        Address = table.Column<string>(
                            type: "varchar(500)",
                            maxLength: 500,
                            nullable: true
                        ),
                        City = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Country = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Manager = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        BankName = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        IBAN = table.Column<string>(
                            type: "varchar(35)",
                            maxLength: 35,
                            nullable: true
                        ),
                        BIC = table.Column<string>(
                            type: "varchar(15)",
                            maxLength: 15,
                            nullable: true
                        ),
                        Email = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        PhoneNumber = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
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
                        table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Orders",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        OrderDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        Status = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: false
                        ),
                        CompletedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        CancelledDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        ShippingAddress = table.Column<string>(
                            type: "varchar(500)",
                            maxLength: 500,
                            nullable: true
                        ),
                        CustomerId = table.Column<Guid>(type: "char(36)", nullable: true),
                        Customer_Name = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Customer_CompanyName = table.Column<string>(
                            type: "varchar(200)",
                            maxLength: 200,
                            nullable: true
                        ),
                        Customer_Email = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Customer_PhoneNumber = table.Column<string>(
                            type: "varchar(15)",
                            maxLength: 15,
                            nullable: true
                        ),
                        Customer_Address = table.Column<string>(
                            type: "varchar(500)",
                            maxLength: 500,
                            nullable: true
                        ),
                        Customer_City = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Customer_Country = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Customer_BankName = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Customer_IBAN = table.Column<string>(
                            type: "varchar(35)",
                            maxLength: 35,
                            nullable: true
                        ),
                        Customer_BIC = table.Column<string>(
                            type: "varchar(15)",
                            maxLength: 15,
                            nullable: true
                        ),
                        Customer_VATNumber = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        Customer_UIC = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
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
                        table.PrimaryKey("PK_Orders", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Invoices",
                    columns: table => new
                    {
                        Id = table
                            .Column<int>(type: "int", nullable: false)
                            .Annotation(
                                "MySQL:ValueGenerationStrategy",
                                MySQLValueGenerationStrategy.IdentityColumn
                            ),
                        InvoiceDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        OrderId = table.Column<Guid>(type: "char(36)", nullable: false),
                        InvoiceStatus = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
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
                        table.PrimaryKey("PK_Invoices", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Invoices_Orders_OrderId",
                            column: x => x.OrderId,
                            principalTable: "Orders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict
                        );
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "OrderItems",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        OrderId = table.Column<Guid>(type: "char(36)", nullable: false),
                        ProductId = table.Column<Guid>(type: "char(36)", nullable: false),
                        SKU = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        Name = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        UnitOfMeasure = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        ListPrice = table.Column<decimal>(
                            type: "decimal(18,2)",
                            precision: 18,
                            scale: 2,
                            nullable: true
                        ),
                        UnitPrice = table.Column<decimal>(
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
                        Cost = table.Column<decimal>(
                            type: "decimal(18,2)",
                            precision: 18,
                            scale: 2,
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OrderItems", x => x.Id);
                        table.ForeignKey(
                            name: "FK_OrderItems_Orders_OrderId",
                            column: x => x.OrderId,
                            principalTable: "Orders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "OrderItemBatches",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        OrderItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                        BatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                        Lot = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        ExpirationDate = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                        Quantity = table.Column<int>(type: "int", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OrderItemBatches", x => x.Id);
                        table.ForeignKey(
                            name: "FK_OrderItemBatches_OrderItems_OrderItemId",
                            column: x => x.OrderItemId,
                            principalTable: "OrderItems",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade
                        );
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemBatches_OrderItemId_BatchId",
                table: "OrderItemBatches",
                columns: new[] { "OrderItemId", "BatchId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_ProductId",
                table: "OrderItems",
                columns: new[] { "OrderId", "ProductId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CompanySettings");

            migrationBuilder.DropTable(name: "Invoices");

            migrationBuilder.DropTable(name: "OrderItemBatches");

            migrationBuilder.DropTable(name: "OrderItems");

            migrationBuilder.DropTable(name: "Orders");
        }
    }
}
