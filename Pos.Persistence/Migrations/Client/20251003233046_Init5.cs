using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class Init5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAtUtc",
                table: "StockDocs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToLocationId",
                table: "StockDocs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToLocationType",
                table: "StockDocs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferNo",
                table: "StockDocs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransferStatus",
                table: "StockDocs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockDocLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockDocId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    SkuSnapshot = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ItemNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    QtyExpected = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QtyReceived = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    UnitCostExpected = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    VarianceNote = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "X''")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocLines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocs_TransferNo",
                table: "StockDocs",
                column: "TransferNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocLines_ItemId_StockDocId",
                table: "StockDocLines",
                columns: new[] { "ItemId", "StockDocId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocLines_StockDocId",
                table: "StockDocLines",
                column: "StockDocId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockDocLines");

            migrationBuilder.DropIndex(
                name: "IX_StockDocs_TransferNo",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "ToLocationId",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "ToLocationType",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "TransferNo",
                table: "StockDocs");

            migrationBuilder.DropColumn(
                name: "TransferStatus",
                table: "StockDocs");
        }
    }
}
