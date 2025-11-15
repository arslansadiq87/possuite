using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class gl_upgradation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GlEntries_AccountId",
                table: "GlEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "ChainId",
                table: "GlEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "DocNo",
                table: "GlEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "DocSubType",
                table: "GlEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "GlEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsEffective",
                table: "GlEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PartyId",
                table: "GlEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_AccountId_EffectiveDate",
                table: "GlEntries",
                columns: new[] { "AccountId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_ChainId_IsEffective",
                table: "GlEntries",
                columns: new[] { "ChainId", "IsEffective" });

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_DocType_DocId",
                table: "GlEntries",
                columns: new[] { "DocType", "DocId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_PartyId",
                table: "GlEntries",
                column: "PartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GlEntries_AccountId_EffectiveDate",
                table: "GlEntries");

            migrationBuilder.DropIndex(
                name: "IX_GlEntries_ChainId_IsEffective",
                table: "GlEntries");

            migrationBuilder.DropIndex(
                name: "IX_GlEntries_DocType_DocId",
                table: "GlEntries");

            migrationBuilder.DropIndex(
                name: "IX_GlEntries_PartyId",
                table: "GlEntries");

            migrationBuilder.DropColumn(
                name: "ChainId",
                table: "GlEntries");

            migrationBuilder.DropColumn(
                name: "DocNo",
                table: "GlEntries");

            migrationBuilder.DropColumn(
                name: "DocSubType",
                table: "GlEntries");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "GlEntries");

            migrationBuilder.DropColumn(
                name: "IsEffective",
                table: "GlEntries");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "GlEntries");

            migrationBuilder.CreateIndex(
                name: "IX_GlEntries_AccountId",
                table: "GlEntries",
                column: "AccountId");
        }
    }
}
