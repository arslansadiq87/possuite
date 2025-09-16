using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialClean11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CounterSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: false),
                    NextInvoiceNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SaleId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Qty = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPct = table.Column<decimal>(type: "TEXT", nullable: true),
                    DiscountAmt = table.Column<decimal>(type: "TEXT", nullable: true),
                    TaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    TaxRatePct = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnitNet = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineNet = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    QtyChange = table.Column<int>(type: "INTEGER", nullable: false),
                    RefType = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<int>(type: "INTEGER", nullable: true),
                    Ts = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TillSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: false),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenTs = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "TEXT", nullable: false),
                    CloseTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeclaredCash = table.Column<decimal>(type: "TEXT", nullable: true),
                    OverShort = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: "BLOB", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Sku = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Barcode = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultTaxRatePct = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultDiscountPct = table.Column<decimal>(type: "TEXT", nullable: true),
                    DefaultDiscountAmt = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    Variant1Name = table.Column<string>(type: "TEXT", nullable: true),
                    Variant1Value = table.Column<string>(type: "TEXT", nullable: true),
                    Variant2Name = table.Column<string>(type: "TEXT", nullable: true),
                    Variant2Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ts = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: false),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: false),
                    TillSessionId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsReturn = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalSaleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisedFromSaleId = table.Column<int>(type: "INTEGER", nullable: true),
                    RevisedToSaleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    HoldTag = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InvoiceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceDiscountPct = table.Column<decimal>(type: "TEXT", nullable: true),
                    InvoiceDiscountAmt = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    InvoiceDiscountValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountBeforeTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CashierId = table.Column<int>(type: "INTEGER", nullable: false),
                    SalesmanId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerKind = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerPhone = table.Column<string>(type: "TEXT", nullable: true),
                    CashAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CardAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    EReceiptToken = table.Column<string>(type: "TEXT", nullable: true),
                    EReceiptUrl = table.Column<string>(type: "TEXT", nullable: true),
                    InvoiceFooter = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Users_CashierId",
                        column: x => x.CashierId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Users_SalesmanId",
                        column: x => x.SalesmanId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CounterSequences_CounterId",
                table: "CounterSequences",
                column: "CounterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_ProductId",
                table: "Items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CashierId",
                table: "Sales",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CounterId_InvoiceNumber",
                table: "Sales",
                columns: new[] { "CounterId", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CounterId_InvoiceNumber_Revision",
                table: "Sales",
                columns: new[] { "CounterId", "InvoiceNumber", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_IsReturn",
                table: "Sales",
                column: "IsReturn");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SalesmanId",
                table: "Sales",
                column: "SalesmanId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Status",
                table: "Sales",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CounterSequences");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "StockEntries");

            migrationBuilder.DropTable(
                name: "TillSessions");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
