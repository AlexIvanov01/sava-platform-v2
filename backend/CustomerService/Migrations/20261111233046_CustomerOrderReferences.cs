using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerService.Migrations
{
    /// <inheritdoc />
    public partial class CustomerOrderReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
