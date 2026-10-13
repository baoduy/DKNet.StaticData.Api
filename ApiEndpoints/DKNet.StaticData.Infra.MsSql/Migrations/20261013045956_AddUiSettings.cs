using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DKNet.StaticData.Infra.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddUiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "SettingGroups",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UiSettings",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SettingKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SettingType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UiSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UiSettings_SettingGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "settings",
                        principalTable: "SettingGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettingGroups_OwnedBy_CreatedOn",
                schema: "settings",
                table: "SettingGroups",
                columns: new[] { "OwnedBy", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SettingGroups_OwnedBy_Name",
                schema: "settings",
                table: "SettingGroups",
                columns: new[] { "OwnedBy", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UiSettings_GroupId",
                schema: "settings",
                table: "UiSettings",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UiSettings_OwnedBy_CreatedOn",
                schema: "settings",
                table: "UiSettings",
                columns: new[] { "OwnedBy", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_UiSettings_OwnedBy_SettingKey",
                schema: "settings",
                table: "UiSettings",
                columns: new[] { "OwnedBy", "SettingKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UiSettings",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SettingGroups",
                schema: "settings");
        }
    }
}
