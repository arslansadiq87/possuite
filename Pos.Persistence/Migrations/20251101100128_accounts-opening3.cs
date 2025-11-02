using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class accountsopening3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GlEntries_Accounts_AccountId",
                table: "GlEntries");

            migrationBuilder.DropTable(
                name: "OpeningBalanceLines");

            migrationBuilder.DropTable(
                name: "OpeningBalances");

            migrationBuilder.DropIndex(
                name: "IX_GlEntries_DocType_DocId",
                table: "GlEntries");

            migrationBuilder.DropIndex(
                name: "IX_GlEntries_TsUtc",
                table: "GlEntries");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Code",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "Normal",
                table: "Accounts",
                newName: "NormalSide");

            migrationBuilder.RenameColumn(
                name: "IsOutletScoped",
                table: "Accounts",
                newName: "IsSystem");

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Parties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Debit",
                table: "GlEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Credit",
                table: "GlEntries",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<bool>(
                name: "AllowPosting",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHeader",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningLocked",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningCredit",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningDebit",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "OutletId",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Journals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TsUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    RefType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RefId = table.Column<int>(type: "INTEGER", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Journals_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JournalId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    PartyId = table.Column<int>(type: "INTEGER", nullable: true),
                    Debit = table.Column<decimal>(type: "TEXT", nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_Journals_JournalId",
                        column: x => x.JournalId,
                        principalTable: "Journals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalLines_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parties_AccountId",
                table: "Parties",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_OutletId_Code",
                table: "Accounts",
                columns: new[] { "OutletId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountId",
                table: "JournalLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_JournalId",
                table: "JournalLines",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_PartyId",
                table: "JournalLines",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Journals_OutletId",
                table: "Journals",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_Journals_TsUtc",
                table: "Journals",
                column: "TsUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Outlets_OutletId",
                table: "Accounts",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GlEntries_Accounts_AccountId",
                table: "GlEntries",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Parties_Accounts_AccountId",
                table: "Parties",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Outlets_OutletId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_GlEntries_Accounts_AccountId",
                table: "GlEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Parties_Accounts_AccountId",
                table: "Parties");

            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "Journals");

            migrationBuilder.DropIndex(
                name: "IX_Parties_AccountId",
                table: "Parties");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_OutletId_Code",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "AllowPosting",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsHeader",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsOpeningLocked",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OpeningCredit",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OpeningDebit",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OutletId",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "NormalSide",
                table: "Accounts",
                newName: "Normal");

            migrationBuilder.RenameColumn(
                name: "IsSystem",
                table: "Accounts",
                newName: "IsOutletScoped");

            migrationBuilder.AlterColumn<decimal>(
                name: "Debit",
                table: "GlEntries",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<decimal>(
                name: "Credit",
                table: "GlEntries",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.CreateTable(
                name: "OpeningBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AsOfDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsPosted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningBalanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLines_OpeningBalances_OpeningBalanceId",
                        column: x => x.OpeningBalanceId,
                        principalTable: "OpeningBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_DocType_DocId",
                table: "GlEntries",
                columns: new[] { "DocType", "DocId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_TsUtc",
                table: "GlEntries",
                column: "TsUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Code",
                table: "Accounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLines_AccountId",
                table: "OpeningBalanceLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLines_OpeningBalanceId",
                table: "OpeningBalanceLines",
                column: "OpeningBalanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_GlEntries_Accounts_AccountId",
                table: "GlEntries",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
