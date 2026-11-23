using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RenameForumsOsfr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Category", "Title" },
                values: new object[] { "Open Source Free Realms Game Discussion", "Open Source Free Realms Discussion" });

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 3,
                column: "Category",
                value: "Open Source Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 4,
                column: "Category",
                value: "Open Source Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 5,
                column: "Category",
                value: "Open Source Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 6,
                column: "Category",
                value: "Open Source Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 7,
                column: "Category",
                value: "Open Source Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Category", "Description" },
                values: new object[] { "Open Source Free Realms Game Discussion", "A place to talk about everything not related to Open Source Free Realms." });

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 9,
                column: "Description",
                value: "Talk about decks, cards and strategies for the Open Source Free Realms Trading Card Game.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Category", "Title" },
                values: new object[] { "Free Realms Game Discussion", "Free Realms Discussion" });

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 3,
                column: "Category",
                value: "Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 4,
                column: "Category",
                value: "Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 5,
                column: "Category",
                value: "Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 6,
                column: "Category",
                value: "Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 7,
                column: "Category",
                value: "Free Realms Game Discussion");

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Category", "Description" },
                values: new object[] { "Free Realms Game Discussion", "A place to talk about everything not related to Free Realms." });

            migrationBuilder.UpdateData(
                table: "Forums",
                keyColumn: "Id",
                keyValue: 9,
                column: "Description",
                value: "Talk about decks, cards and strategies for the Free Realms Trading Card Game.");
        }
    }
}
