using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfilePrivacy",
                columns: table => new
                {
                    CharacterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Messages = table.Column<int>(type: "INTEGER", nullable: false),
                    Gallery = table.Column<int>(type: "INTEGER", nullable: false),
                    Characters = table.Column<int>(type: "INTEGER", nullable: false),
                    Friends = table.Column<int>(type: "INTEGER", nullable: false),
                    News = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfilePrivacy", x => x.CharacterId);
                    table.ForeignKey(
                        name: "FK_ProfilePrivacy_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfilePrivacy");
        }
    }
}
