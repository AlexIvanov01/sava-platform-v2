using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class Innit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Customers",
                    columns: table => new
                    {
                        ExternalId = table.Column<Guid>(type: "char(36)", nullable: false),
                        Name = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        CompanyName = table.Column<string>(
                            type: "varchar(200)",
                            maxLength: 200,
                            nullable: true
                        ),
                        Email = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        PhoneNumber = table.Column<string>(
                            type: "varchar(15)",
                            maxLength: 15,
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
                        VATNumber = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                        UIC = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Customers", x => x.ExternalId);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Items",
                    columns: table => new
                    {
                        ExternalBatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                        ExternalProductId = table.Column<Guid>(type: "char(36)", nullable: true),
                        Name = table.Column<string>(
                            type: "varchar(100)",
                            maxLength: 100,
                            nullable: true
                        ),
                        Price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                        Lot = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        ExpirationDate = table.Column<DateTime>(
                            type: "datetime(6)",
                            nullable: true
                        ),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Items", x => x.ExternalBatchId);
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
                        ShippedDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        ShippingAddress = table.Column<string>(
                            type: "varchar(500)",
                            maxLength: 500,
                            nullable: true
                        ),
                        OrderStatus = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        CustomerId = table.Column<Guid>(type: "char(36)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Orders", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Orders_Customers_CustomerId",
                            column: x => x.CustomerId,
                            principalTable: "Customers",
                            principalColumn: "ExternalId",
                            onDelete: ReferentialAction.Restrict
                        );
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
                        OrderId = table.Column<Guid>(type: "char(36)", nullable: false),
                        ItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                        OrderItemQuantity = table.Column<int>(type: "int", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OrderItems", x => new { x.OrderId, x.ItemId });
                        table.ForeignKey(
                            name: "FK_OrderItems_Items_ItemId",
                            column: x => x.ItemId,
                            principalTable: "Items",
                            principalColumn: "ExternalBatchId",
                            onDelete: ReferentialAction.Restrict
                        );
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

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ItemId",
                table: "OrderItems",
                column: "ItemId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Invoices");

            migrationBuilder.DropTable(name: "OrderItems");

            migrationBuilder.DropTable(name: "Items");

            migrationBuilder.DropTable(name: "Orders");

            migrationBuilder.DropTable(name: "Customers");
        }
    }
}
