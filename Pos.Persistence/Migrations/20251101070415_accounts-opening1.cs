using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class accountsopening1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpeningBalance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AsOfDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    IsPosted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpeningBalanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLine_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLine_OpeningBalance_OpeningBalanceId",
                        column: x => x.OpeningBalanceId,
                        principalTable: "OpeningBalance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLine_AccountId",
                table: "OpeningBalanceLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLine_OpeningBalanceId",
                table: "OpeningBalanceLine",
                column: "OpeningBalanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpeningBalanceLine");

            migrationBuilder.DropTable(
                name: "OpeningBalance");
        }
    }
}
