using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class Init8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_StockEntry_StockDoc_Requirement",
                table: "StockEntries",
                sql: "CASE  WHEN [RefType] IN ('Opening','TransferOut','TransferIn') THEN [StockDocId] IS NOT NULL  ELSE 1 END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockEntry_StockDoc_Requirement",
                table: "StockEntries");
        }
    }
}
