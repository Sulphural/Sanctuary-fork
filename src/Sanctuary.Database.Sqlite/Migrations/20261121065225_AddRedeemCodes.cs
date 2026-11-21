using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRedeemCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RedeemCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Coins = table.Column<int>(type: "INTEGER", nullable: false),
                    StationCash = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemDefinition = table.Column<int>(type: "INTEGER", nullable: true),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxUses = table.Column<int>(type: "INTEGER", nullable: false),
                    Uses = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedeemCodes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "CodeRedemptions",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRedemptions", x => new { x.Code, x.UserId });
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_RedeemCodes_Code",
                        column: x => x.Code,
                        principalTable: "RedeemCodes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_CharacterId",
                table: "CodeRedemptions",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_UserId",
                table: "CodeRedemptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeRedemptions");

            migrationBuilder.DropTable(
                name: "RedeemCodes");
        }
    }
}
