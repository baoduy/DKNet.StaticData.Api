using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DKNet.StaticData.Infra.Postgres.Migrations
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
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileGroups",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
