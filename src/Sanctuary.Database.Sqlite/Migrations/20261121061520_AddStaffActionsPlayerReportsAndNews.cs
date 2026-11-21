using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffActionsPlayerReportsAndNews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsPosts",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()"),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    AuthorUsername = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerReports",
                columns: table => new
                {
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ReporterUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerReports", x => new { x.CharacterId, x.ReporterUserId });
                    table.ForeignKey(
                        name: "FK_PlayerReports_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffActions",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()"),
                    StaffUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    StaffUsername = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    TargetCharacterId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsPosts_Created",
                table: "NewsPosts",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerReports_ReporterUserId",
                table: "PlayerReports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffActions_Created",
                table: "StaffActions",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_StaffActions_TargetUserId",
                table: "StaffActions",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsPosts");

            migrationBuilder.DropTable(
                name: "PlayerReports");

            migrationBuilder.DropTable(
                name: "StaffActions");
        }
    }
}
