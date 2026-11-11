using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Migrations
{
    /// <inheritdoc />
    public partial class StockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_StockMovements_BatchId",
                table: "StockMovements",
                column: "BatchId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrderId_BatchId",
                table: "StockMovements",
                columns: new[] { "OrderId", "BatchId" }
            );

            // Existing stock enters the ledger as an opening balance, so each batch's Quantity
            // equals the sum of its movements from the start.
            migrationBuilder.Sql(
                @"INSERT INTO StockMovements (Id, BatchId, Type, Quantity, BalanceAfter, Note, CreatedAt, CreatedBy)
                  SELECT UUID(), Id, 'OpeningBalance', Quantity, Quantity,
                         'Quantity when the stock ledger was introduced', UTC_TIMESTAMP(6), 'system'
                  FROM ProductBatches
                  WHERE Quantity IS NOT NULL AND Quantity <> 0;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StockMovements");
        }
    }
}
