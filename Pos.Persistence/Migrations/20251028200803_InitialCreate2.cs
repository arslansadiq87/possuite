using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAtUtc",
                table: "StockDocs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostedByUserId",
                table: "StockDocs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "StockDocs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "StockDocs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "StockDocs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpeningStockDraftLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockDocId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningStockDraftLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpeningStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationType = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    TsUtc = table.Column<DateTime>(type: "datetime", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PostedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LockedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VoidedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningStocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpeningStockLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpeningStockId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningStockLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningStockLines_OpeningStocks_OpeningStockId",
                        column: x => x.OpeningStockId,
                        principalTable: "OpeningStocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningStockDraftLines_StockDocId",
                table: "OpeningStockDraftLines",
                column: "StockDocId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningStockLines_OpeningStockId",
                table: "OpeningStockLines",
                column: "OpeningStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpeningStockDraftLines");

            migrationBuilder.DropTable(
                name: "OpeningStockLines");

            migrationBuilder.DropTable(
                name: "OpeningStocks");

            migrationBuilder.DropColumn(
                name: "PostedAtUtc",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "StockDocs");
        }
    }
}
