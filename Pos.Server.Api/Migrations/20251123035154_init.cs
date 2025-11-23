using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Pos.Server.Api.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<long>(type: "bigint", nullable: false),
                    Entity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    TsUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTerminal = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TerminalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastToken = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerCursors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerChanges_Token",
                table: "ServerChanges",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerCursors_TerminalId",
                table: "ServerCursors",
                column: "TerminalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerChanges");

            migrationBuilder.DropTable(
                name: "ServerCursors");
        }
    }
}
