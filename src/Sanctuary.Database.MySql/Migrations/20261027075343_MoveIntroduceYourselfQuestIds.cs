using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanctuary.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class MoveIntroduceYourselfQuestIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("INSERT INTO CharacterQuests (QuestId, CharacterId, Completed, CurrentGoalId, Started) SELECT 90563, CharacterId, Completed, CurrentGoalId, Started FROM CharacterQuests WHERE QuestId = 2563");
            migrationBuilder.Sql("UPDATE CharacterQuestProgress SET QuestId = 90563 WHERE QuestId = 2563");
            migrationBuilder.Sql("DELETE FROM CharacterQuests WHERE QuestId = 2563");
            migrationBuilder.Sql("UPDATE Characters SET ActiveQuestId = 90563 WHERE ActiveQuestId = 2563");

            migrationBuilder.Sql("INSERT INTO CharacterQuests (QuestId, CharacterId, Completed, CurrentGoalId, Started) SELECT 90564, CharacterId, Completed, CurrentGoalId, Started FROM CharacterQuests WHERE QuestId = 2564");
            migrationBuilder.Sql("UPDATE CharacterQuestProgress SET QuestId = 90564 WHERE QuestId = 2564");
            migrationBuilder.Sql("DELETE FROM CharacterQuests WHERE QuestId = 2564");
            migrationBuilder.Sql("UPDATE Characters SET ActiveQuestId = 90564 WHERE ActiveQuestId = 2564");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("INSERT INTO CharacterQuests (QuestId, CharacterId, Completed, CurrentGoalId, Started) SELECT 2563, CharacterId, Completed, CurrentGoalId, Started FROM CharacterQuests WHERE QuestId = 90563");
            migrationBuilder.Sql("UPDATE CharacterQuestProgress SET QuestId = 2563 WHERE QuestId = 90563");
            migrationBuilder.Sql("DELETE FROM CharacterQuests WHERE QuestId = 90563");
            migrationBuilder.Sql("UPDATE Characters SET ActiveQuestId = 2563 WHERE ActiveQuestId = 90563");

            migrationBuilder.Sql("INSERT INTO CharacterQuests (QuestId, CharacterId, Completed, CurrentGoalId, Started) SELECT 2564, CharacterId, Completed, CurrentGoalId, Started FROM CharacterQuests WHERE QuestId = 90564");
            migrationBuilder.Sql("UPDATE CharacterQuestProgress SET QuestId = 2564 WHERE QuestId = 90564");
            migrationBuilder.Sql("DELETE FROM CharacterQuests WHERE QuestId = 90564");
            migrationBuilder.Sql("UPDATE Characters SET ActiveQuestId = 2564 WHERE ActiveQuestId = 90564");
        }
    }
}
