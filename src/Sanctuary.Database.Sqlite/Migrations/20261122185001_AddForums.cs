using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddForums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Forums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CategoryOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    StaffThreadsOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    MembersOnly = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Forums", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumThreads",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ForumId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AuthorUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    AuthorCharacterId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()"),
                    LastPostAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()"),
                    LastPostId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    LastPostCharacterId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    LastPostName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReplyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSticky = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumThreads_Characters_AuthorCharacterId",
                        column: x => x.AuthorCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ForumThreads_Forums_ForumId",
                        column: x => x.ForumId,
                        principalTable: "Forums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ForumThreads_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ForumPosts",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    AuthorCharacterId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()"),
                    Edited = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EditedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumPosts_Characters_AuthorCharacterId",
                        column: x => x.AuthorCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ForumPosts_ForumThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "ForumThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ForumPosts_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ForumPostReports",
                columns: table => new
                {
                    PostId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ReporterUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "DATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumPostReports", x => new { x.PostId, x.ReporterUserId });
                    table.ForeignKey(
                        name: "FK_ForumPostReports_ForumPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "ForumPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ForumPostReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Forums",
                columns: new[] { "Id", "Category", "CategoryOrder", "Description", "DisplayOrder", "MembersOnly", "StaffThreadsOnly", "Title" },
                values: new object[,]
                {
                    { 1, "News & Announcements", 1, "Official news and announcements from the Open Source Free Realms team.", 1, false, true, "News & Announcements" },
                    { 2, "Free Realms Game Discussion", 2, "A forum for everyone to discuss the game!", 1, false, false, "Free Realms Discussion" },
                    { 3, "Free Realms Game Discussion", 2, "Check here to read what's new in the community and to participate in fun activities and discussions.", 2, false, false, "Community News" },
                    { 4, "Free Realms Game Discussion", 2, "[Members Only] Discuss general game-related topics", 3, true, false, "Members Only Discussion" },
                    { 5, "Free Realms Game Discussion", 2, "Have an item to trade? Questions about collections? Look here!", 4, false, false, "Trade and Collections" },
                    { 6, "Free Realms Game Discussion", 2, "Have a guild you want to promote? Questions about joining a guild? This is the place!", 5, false, false, "Guild Recruitment and Discussion" },
                    { 7, "Free Realms Game Discussion", 2, "Do you have a great idea for the game or run into something that isn't right? Let us know about it in this forum!", 6, false, false, "Suggestions and Bugs" },
                    { 8, "Free Realms Game Discussion", 2, "A place to talk about everything not related to Free Realms.", 7, false, false, "Off-Topic Talk" },
                    { 9, "Trading Card Game", 3, "Talk about decks, cards and strategies for the Free Realms Trading Card Game.", 1, false, false, "Trading Card Game Discussion" },
                    { 10, "Trading Card Game", 3, "Found a problem with the Trading Card Game? Report it here.", 2, false, false, "Trading Card Game Support and Bugs" },
                    { 11, "Technical Support", 4, "Trouble connecting, lag or slow loading? Ask for help here.", 1, false, false, "Connection and Performance Issues" },
                    { 12, "Technical Support", 4, "Crashes, launcher and plugin problems, and other technical issues.", 2, false, false, "Technical Issues and Crashes" },
                    { 13, "Technical Support", 4, "Questions about your account, membership and Station Cash.", 3, false, false, "Account Issues" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostReports_ReporterUserId",
                table: "ForumPostReports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_AuthorCharacterId",
                table: "ForumPosts",
                column: "AuthorCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_AuthorUserId",
                table: "ForumPosts",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_ThreadId_Id",
                table: "ForumPosts",
                columns: new[] { "ThreadId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_AuthorCharacterId",
                table: "ForumThreads",
                column: "AuthorCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_AuthorUserId",
                table: "ForumThreads",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_ForumId_IsSticky_LastPostId",
                table: "ForumThreads",
                columns: new[] { "ForumId", "IsSticky", "LastPostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForumPostReports");

            migrationBuilder.DropTable(
                name: "ForumPosts");

            migrationBuilder.DropTable(
                name: "ForumThreads");

            migrationBuilder.DropTable(
                name: "Forums");
        }
    }
}
