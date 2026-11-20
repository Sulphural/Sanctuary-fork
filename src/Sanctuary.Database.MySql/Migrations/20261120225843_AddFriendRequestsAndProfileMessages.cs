using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.MySql.Migrations
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
                    CharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TargetCharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, defaultValueSql: "NOW()")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProfileMessages",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Created = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, defaultValueSql: "NOW()"),
                    CharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    AuthorCharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
