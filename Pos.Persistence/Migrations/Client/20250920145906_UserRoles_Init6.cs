using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations.Client
{
    /// <inheritdoc />
    public partial class UserRoles_Init6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CounterBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MachineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OutletId = table.Column<int>(type: "INTEGER", nullable: false),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CounterBindings_Counters_CounterId",
                        column: x => x.CounterId,
                        principalTable: "Counters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CounterBindings_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CounterBindings_CounterId",
                table: "CounterBindings",
                column: "CounterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CounterBindings_MachineId",
                table: "CounterBindings",
                column: "MachineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CounterBindings_OutletId",
                table: "CounterBindings",
                column: "OutletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CounterBindings");
        }
    }
}
