using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace CustomerService.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateTable(
                    name: "InboxMessages",
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
                        AggregateId = table.Column<Guid>(type: "char(36)", nullable: true),
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
                        CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                        ProcessedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_InboxMessages", x => x.Id);
                    }
                )
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_AggregateId",
                table: "InboxMessages",
                column: "AggregateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Sequence",
                table: "InboxMessages",
                column: "Sequence",
                unique: true
            );

            migrationBuilder.Sql(
                "ALTER TABLE `InboxMessages` MODIFY `Sequence` bigint NOT NULL AUTO_INCREMENT;"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Status_CreatedAt",
                table: "InboxMessages",
                columns: new[] { "Status", "CreatedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InboxMessages");
        }
    }
}
