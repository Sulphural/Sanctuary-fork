using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendRequestsAndProfileMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FriendRequests",
                columns: table => new
                {
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    TargetCharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendRequests", x => new { x.CharacterId, x.TargetCharacterId });
                    table.ForeignKey(
                        name: "FK_FriendRequests_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FriendRequests_Characters_TargetCharacterId",
                        column: x => x.TargetCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileMessages",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()"),
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    AuthorCharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileMessages_Characters_AuthorCharacterId",
                        column: x => x.AuthorCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileMessages_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_TargetCharacterId",
                table: "FriendRequests",
                column: "TargetCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileMessages_AuthorCharacterId",
                table: "ProfileMessages",
                column: "AuthorCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileMessages_CharacterId_Created",
                table: "ProfileMessages",
                columns: new[] { "CharacterId", "Created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendRequests");

            migrationBuilder.DropTable(
                name: "ProfileMessages");
        }
    }
}
