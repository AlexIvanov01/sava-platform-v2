using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "OutboxMessages",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "char(36)", nullable: false),
                        // AUTO_INCREMENT is added below: MySQL only accepts it on an indexed column.
                        Sequence = table.Column<long>(type: "bigint", nullable: false),
                        Type = table.Column<string>(
                            type: "varchar(50)",
                            maxLength: 50,
                            nullable: true
                        ),
                        AggregateId = table.Column<Guid>(type: "char(36)", nullable: false),
                        Payload = table.Column<string>(type: "longtext", nullable: true),
                        Status = table.Column<string>(
                            type: "varchar(20)",
                            maxLength: 20,
                            nullable: false
                        ),
                        Attempts = table.Column<int>(type: "int", nullable: false),
                        LastError = table.Column<string>(
                            type: "varchar(2000)",
                            maxLength: 2000,
                            nullable: true
                        ),
                        NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        ProcessedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AggregateId",
                table: "OutboxMessages",
                column: "AggregateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Sequence",
                table: "OutboxMessages",
                column: "Sequence",
                unique: true
            );

            migrationBuilder.Sql(
                "ALTER TABLE `OutboxMessages` MODIFY `Sequence` bigint NOT NULL AUTO_INCREMENT;"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_NextAttemptAt",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OutboxMessages");
        }
    }
}
