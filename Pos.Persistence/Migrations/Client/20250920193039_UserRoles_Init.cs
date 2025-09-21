using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class UserRoles_Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId1",
                table: "Purchases");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.RenameColumn(
                name: "SupplierId1",
                table: "Purchases",
                newName: "PartyId1");

            migrationBuilder.RenameColumn(
                name: "SupplierId",
                table: "Purchases",
                newName: "PartyId");

            migrationBuilder.RenameIndex(
                name: "IX_Purchases_SupplierId1",
                table: "Purchases",
                newName: "IX_Purchases_PartyId1");

            migrationBuilder.RenameIndex(
                name: "IX_Purchases_SupplierId",
                table: "Purchases",
                newName: "IX_Purchases_PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Parties_PartyId",
                table: "Purchases",
                column: "PartyId",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Parties_PartyId1",
                table: "Purchases",
                column: "PartyId1",
                principalTable: "Parties",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Parties_PartyId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Parties_PartyId1",
                table: "Purchases");

            migrationBuilder.RenameColumn(
                name: "PartyId1",
                table: "Purchases",
                newName: "SupplierId1");

            migrationBuilder.RenameColumn(
                name: "PartyId",
                table: "Purchases",
                newName: "SupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_Purchases_PartyId1",
                table: "Purchases",
                newName: "IX_Purchases_SupplierId1");

            migrationBuilder.RenameIndex(
                name: "IX_Purchases_PartyId",
                table: "Purchases",
                newName: "IX_Purchases_SupplierId");

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OpeningBalanceDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_IsActive",
                table: "Suppliers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Name",
                table: "Suppliers",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId",
                table: "Purchases",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Suppliers_SupplierId1",
                table: "Purchases",
                column: "SupplierId1",
                principalTable: "Suppliers",
                principalColumn: "Id");
        }
    }
}
