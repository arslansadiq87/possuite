using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class vouchers1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AmendedAtUtc",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AmendedFromId",
                table: "Vouchers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNo",
                table: "Vouchers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Vouchers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_AmendedFromId",
                table: "Vouchers",
                column: "AmendedFromId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vouchers_AmendedFromId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AmendedAtUtc",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AmendedFromId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "RevisionNo",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "Vouchers");
        }
    }
}
