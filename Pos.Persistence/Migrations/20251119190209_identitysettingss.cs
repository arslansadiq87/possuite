using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class identitysettingss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentitySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: true),
                    OutletDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BusinessNtn = table.Column<string>(type: "TEXT", nullable: true),
                    ShowBusinessNtn = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableFbr = table.Column<bool>(type: "INTEGER", nullable: false),
                    FbrPosId = table.Column<string>(type: "TEXT", nullable: true),
                    LogoPng = table.Column<byte[]>(type: "BLOB", nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValueSql: "randomblob(8)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentitySettings_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySettings_OutletId",
                table: "IdentitySettings",
                column: "OutletId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentitySettings");
        }
    }
}
