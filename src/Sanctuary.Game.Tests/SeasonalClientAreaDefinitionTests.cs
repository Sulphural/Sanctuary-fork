using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class SeasonalClientAreaDefinitionTests
{
    [TestMethod]
    [DataRow(12, 2, false)]
    [DataRow(12, 3, true)]
    [DataRow(12, 31, true)]
    [DataRow(1, 1, true)]
    [DataRow(1, 13, true)]
    [DataRow(1, 14, false)]
    public void IsActive_SpansTheNewYear(int month, int day, bool expected)
    {
        var season = new SeasonalClientAreaDefinition { Name = "SnowDays", StartMonth = 12, StartDay = 3, EndMonth = 1, EndDay = 13 };

        Assert.AreEqual(expected, season.IsActive(new DateTime(2026, month, day)));
    }
}
