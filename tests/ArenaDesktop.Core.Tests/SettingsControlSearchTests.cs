using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

public class SettingsControlSearchTests
{
    [Theory]
    [InlineData("update freq", "Check for updates automatically Every launch Once a week", true)]
    [InlineData("week launch", "Check for updates automatically Every launch Once a week", true)]
    [InlineData("automa freq", "Check for updates automatically Every launch Once a week", true)]
    [InlineData("update daily", "Check for updates automatically Every launch Once a week", false)]
    [InlineData("cookie", "Restore profile settings", false)]
    public void Matches_UsesEveryWordAsAPrefix(string query, string searchableText, bool expected) =>
        Assert.Equal(expected, SettingsControlSearch.Matches(query, searchableText));

    [Fact]
    public void Matches_EmptyQueryOrTextNeverMatches() =>
        Assert.False(SettingsControlSearch.Matches("", "Search settings"));
}
