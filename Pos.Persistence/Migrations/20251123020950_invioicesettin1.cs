using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class invioicesettin1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupBaseFolder",
                table: "InvoiceSettingsScoped");

            migrationBuilder.DropColumn(
                name: "EnableDailyBackup",
                table: "InvoiceSettingsScoped");

            migrationBuilder.DropColumn(
                name: "EnableHourlyBackup",
                table: "InvoiceSettingsScoped");

            migrationBuilder.DropColumn(
                name: "UseServerForBackupRestore",
                table: "InvoiceSettingsScoped");

            migrationBuilder.CreateTable(
                name: "ServerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    OutletCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CounterCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AutoSyncEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PushIntervalSec = table.Column<int>(type: "INTEGER", nullable: false),
                    PullIntervalSec = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableDailyBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableHourlyBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackupBaseFolder = table.Column<string>(type: "TEXT", nullable: true),
                    UseServerForBackupRestore = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ServerSettings",
                columns: new[] { "Id", "ApiKey", "AutoSyncEnabled", "BackupBaseFolder", "BaseUrl", "CounterCode", "EnableDailyBackup", "EnableHourlyBackup", "OutletCode", "PullIntervalSec", "PushIntervalSec", "UseServerForBackupRestore" },
                values: new object[] { 1, null, true, null, null, null, false, false, null, 15, 15, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerSettings");

            migrationBuilder.AddColumn<string>(
                name: "BackupBaseFolder",
                table: "InvoiceSettingsScoped",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableDailyBackup",
                table: "InvoiceSettingsScoped",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableHourlyBackup",
                table: "InvoiceSettingsScoped",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseServerForBackupRestore",
                table: "InvoiceSettingsScoped",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
