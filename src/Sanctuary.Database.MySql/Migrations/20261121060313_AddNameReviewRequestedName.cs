using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddNameReviewRequestedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestedFirstName",
                table: "NameReviews",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RequestedLastName",
                table: "NameReviews",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE NameReviews SET RequestedFirstName = (SELECT FirstName FROM Characters WHERE Characters.Id = NameReviews.CharacterId), RequestedLastName = COALESCE((SELECT LastName FROM Characters WHERE Characters.Id = NameReviews.CharacterId), '')");

            migrationBuilder.CreateIndex(
                name: "IX_NameReviews_RequestedFirstName_RequestedLastName",
                table: "NameReviews",
                columns: new[] { "RequestedFirstName", "RequestedLastName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NameReviews_RequestedFirstName_RequestedLastName",
                table: "NameReviews");

            migrationBuilder.DropColumn(
                name: "RequestedFirstName",
                table: "NameReviews");

            migrationBuilder.DropColumn(
                name: "RequestedLastName",
                table: "NameReviews");
        }
    }
}
