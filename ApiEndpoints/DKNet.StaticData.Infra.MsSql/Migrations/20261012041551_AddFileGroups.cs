using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DKNet.StaticData.Infra.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddFileGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                schema: "files",
                table: "StoredFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileGroups",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalRef = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OwnedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Purpose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_GroupId",
                schema: "files",
                table: "StoredFiles",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FileGroups_OwnedBy_CreatedOn",
                schema: "files",
                table: "FileGroups",
                columns: new[] { "OwnedBy", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_FileGroups_OwnedBy_ExternalRef",
                schema: "files",
                table: "FileGroups",
                columns: new[] { "OwnedBy", "ExternalRef" });

            migrationBuilder.CreateIndex(
                name: "IX_FileGroups_OwnedBy_Purpose",
                schema: "files",
                table: "FileGroups",
                columns: new[] { "OwnedBy", "Purpose" });

            migrationBuilder.AddForeignKey(
                name: "FK_StoredFiles_FileGroups_GroupId",
                schema: "files",
                table: "StoredFiles",
                column: "GroupId",
                principalSchema: "files",
                principalTable: "FileGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoredFiles_FileGroups_GroupId",
                schema: "files",
                table: "StoredFiles");

            migrationBuilder.DropTable(
                name: "FileGroups",
                schema: "files");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_GroupId",
                schema: "files",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "files",
                table: "StoredFiles");
        }
    }
}
