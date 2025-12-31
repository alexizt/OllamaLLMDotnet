using System;
using Xunit;
using System.Collections.Generic;
using App = Program; // Program is in the top-level namespace of the console app

public class ProgramTests
{
    [Fact]
    public void ExtractJsonFromText_ExtractsObject()
    {
        var text = "Intro text\n{\"a\":1,\"b\":2}\nOutro";
        var json = App.ExtractJsonFromText(text);
        Assert.Equal("{\"a\":1,\"b\":2}", json);
    }

    [Fact]
    public void ExtractJsonFromText_ExtractsArray()
    {
        var text = "Start [1,2,3] end";
        var json = App.ExtractJsonFromText(text);
        Assert.Equal("[1,2,3]", json);
    }

    [Fact]
    public void ExtractJsonFromText_NoJson_ReturnsOriginal()
    {
        var text = "No json here";
        var json = App.ExtractJsonFromText(text);
        Assert.Equal(text, json);
    }

    [Fact]
    public void ParseStructuredSummary_ValidJson_ReturnsObject()
    {
        var json = "{\"Title\":\"T\",\"Summary\":\"S\",\"key_points\":[\"a\"],\"language\":\"en\",\"word_count\":10}";
        var obj = App.ParseStructuredSummary(json);
        Assert.NotNull(obj);
        Assert.Equal("T", obj.Title);
        Assert.Equal("S", obj.Summary);
        Assert.Equal("en", obj.Language);
        Assert.Equal(10, obj.Word_Count);
        Assert.Single(obj.Key_Points);
    }

    [Fact]
    public void ParseStructuredSummary_InvalidJson_ReturnsNull()
    {
        var json = "{not a valid json}";
        var obj = App.ParseStructuredSummary(json);
        Assert.Null(obj);
    }

    [Fact]
    public void SplitIntoChunks_BasicSplitting()
    {
        var text = string.Join(" ", new string[500].Select((_, i) => "word" + i));
        var chunks = App.SplitIntoChunks(text, 200);
        Assert.NotEmpty(chunks);
        // Ensure no chunk exceeds limit (rough check)
        foreach (var c in chunks)
            Assert.True(c.Length <= 200);

        // Reconstruct and ensure the important parts are present
        var reconstructed = string.Join(" ", chunks);
        Assert.Contains("word0", reconstructed);
        Assert.Contains("word499", reconstructed);
    }
}