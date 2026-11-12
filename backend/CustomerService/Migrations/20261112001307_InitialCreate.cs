using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerService.Migrations
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
                    name: "CustomerOrders",
                    columns: table => new
                    {
                        OrderId = table.Column<Guid>(type: "char(36)", nullable: false),
                        CustomerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_CustomerOrders", x => x.OrderId);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder
                .CreateTable(
                    name: "Customers",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
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
                        table.PrimaryKey("PK_Customers", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrders_CustomerId",
                table: "CustomerOrders",
                column: "CustomerId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerOrders");

            migrationBuilder.DropTable(name: "Customers");
        }
    }
}
