using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class AuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Orders",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Orders",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Orders",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Orders",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Invoices",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Invoices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Invoices",
                type: "datetime(6)",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Invoices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CreatedAt", table: "Orders");

            migrationBuilder.DropColumn(name: "CreatedBy", table: "Orders");

            migrationBuilder.DropColumn(name: "UpdatedAt", table: "Orders");

            migrationBuilder.DropColumn(name: "UpdatedBy", table: "Orders");

            migrationBuilder.DropColumn(name: "CreatedAt", table: "Invoices");

            migrationBuilder.DropColumn(name: "CreatedBy", table: "Invoices");

            migrationBuilder.DropColumn(name: "UpdatedAt", table: "Invoices");

            migrationBuilder.DropColumn(name: "UpdatedBy", table: "Invoices");
        }
    }
}
