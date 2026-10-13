using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DKNet.StaticData.Infra.Postgres.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SettingKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SettingType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
