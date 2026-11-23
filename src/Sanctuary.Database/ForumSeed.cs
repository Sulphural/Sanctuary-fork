using Sanctuary.Database.Entities;

namespace Sanctuary.Database;

public static class ForumSeed
{
    private const string News = "News & Announcements";
    private const string Game = "Open Source Free Realms Game Discussion";
    private const string Tcg = "Trading Card Game";
    private const string Support = "Technical Support";

    public static readonly DbForum[] Forums =
    [
        new() { Id = 1, Category = News, CategoryOrder = 1, DisplayOrder = 1, StaffThreadsOnly = true, Title = "News & Announcements", Description = "Official news and announcements from the Open Source Free Realms team." },

        new() { Id = 2, Category = Game, CategoryOrder = 2, DisplayOrder = 1, Title = "Open Source Free Realms Discussion", Description = "A forum for everyone to discuss the game!" },
        new() { Id = 3, Category = Game, CategoryOrder = 2, DisplayOrder = 2, Title = "Community News", Description = "Check here to read what's new in the community and to participate in fun activities and discussions." },
        new() { Id = 4, Category = Game, CategoryOrder = 2, DisplayOrder = 3, MembersOnly = true, Title = "Members Only Discussion", Description = "[Members Only] Discuss general game-related topics" },
        new() { Id = 5, Category = Game, CategoryOrder = 2, DisplayOrder = 4, Title = "Trade and Collections", Description = "Have an item to trade? Questions about collections? Look here!" },
        new() { Id = 6, Category = Game, CategoryOrder = 2, DisplayOrder = 5, Title = "Guild Recruitment and Discussion", Description = "Have a guild you want to promote? Questions about joining a guild? This is the place!" },
        new() { Id = 7, Category = Game, CategoryOrder = 2, DisplayOrder = 6, Title = "Suggestions and Bugs", Description = "Do you have a great idea for the game or run into something that isn't right? Let us know about it in this forum!" },
        new() { Id = 8, Category = Game, CategoryOrder = 2, DisplayOrder = 7, Title = "Off-Topic Talk", Description = "A place to talk about everything not related to Open Source Free Realms." },

        new() { Id = 9, Category = Tcg, CategoryOrder = 3, DisplayOrder = 1, Title = "Trading Card Game Discussion", Description = "Talk about decks, cards and strategies for the Open Source Free Realms Trading Card Game." },
        new() { Id = 10, Category = Tcg, CategoryOrder = 3, DisplayOrder = 2, Title = "Trading Card Game Support and Bugs", Description = "Found a problem with the Trading Card Game? Report it here." },

        new() { Id = 11, Category = Support, CategoryOrder = 4, DisplayOrder = 1, Title = "Connection and Performance Issues", Description = "Trouble connecting, lag or slow loading? Ask for help here." },
        new() { Id = 12, Category = Support, CategoryOrder = 4, DisplayOrder = 2, Title = "Technical Issues and Crashes", Description = "Crashes, launcher and plugin problems, and other technical issues." },
        new() { Id = 13, Category = Support, CategoryOrder = 4, DisplayOrder = 3, Title = "Account Issues", Description = "Questions about your account, membership and Station Cash." },
    ];
}
