using System.Reflection;
using System.Text.RegularExpressions;
using Gravitone.Core;

namespace Gravitone.Tests;

// Loc is global state: these tests must not run in parallel with each other.
[Collection("Loc")]
public class LocTests
{
    static Dictionary<string, string> Italian() =>
        (Dictionary<string, string>)typeof(Loc).GetField("It", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void English_returns_the_source_text()
    {
        Loc.Init("en");
        Assert.Equal("Keep in Dock", Loc.T("Keep in Dock"));
        Assert.Equal("Quit Gravitone", Loc.T("Quit {0}", "Gravitone"));
    }

    [Fact]
    public void Italian_translates_and_formats()
    {
        Loc.Init("it");
        try
        {
            Assert.Equal("Mantieni nel Dock", Loc.T("Keep in Dock"));
            Assert.Equal("Esci da Gravitone", Loc.T("Quit {0}", "Gravitone"));
        }
        finally
        {
            Loc.Init("en");
        }
    }

    [Fact]
    public void A_text_without_translation_falls_back_to_English()
    {
        Loc.Init("it");
        try
        {
            Assert.Equal("Something new", Loc.T("Something new"));
        }
        finally
        {
            Loc.Init("en");
        }
    }

    [Fact]
    public void Every_translation_keeps_the_placeholders_of_its_source()
    {
        var placeholder = new Regex(@"\{\d+\}");
        foreach (var (english, italian) in Italian())
        {
            var expected = placeholder.Matches(english).Select(m => m.Value).Order();
            var actual = placeholder.Matches(italian).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"\"{italian}\" does not keep the placeholders of \"{english}\"");
            Assert.False(string.IsNullOrWhiteSpace(italian), $"empty translation for \"{english}\"");
        }
    }

    [Fact]
    public void The_language_setting_offers_system_English_and_Italian()
    {
        Assert.Equal(["", "en", "it"], Loc.Languages);
    }
}
