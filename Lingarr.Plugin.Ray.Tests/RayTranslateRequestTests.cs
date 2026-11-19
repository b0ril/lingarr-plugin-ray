using Lingarr.Plugin.Ray;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class RayTranslateRequestTests
{
    [Fact]
    public void NormalizeQuality_DefaultsBlankToBest()
    {
        Assert.Equal("best", RayTranslateRequest.NormalizeQuality(null));
        Assert.Equal("best", RayTranslateRequest.NormalizeQuality(""));
        Assert.Equal("best", RayTranslateRequest.NormalizeQuality("  "));
    }

    [Theory]
    [InlineData("best", "best")]
    [InlineData("BEST", "best")]
    [InlineData("sunlight", "best")]
    [InlineData("Sunlight", "best")]
    [InlineData("fast", "fast")]
    [InlineData("FAST", "fast")]
    [InlineData("moonlight", "fast")]
    [InlineData("Moonlight", "fast")]
    public void NormalizeQuality_AcceptsBestFastAndAliases(string input, string expected)
    {
        Assert.Equal(expected, RayTranslateRequest.NormalizeQuality(input));
    }

    [Fact]
    public void NormalizeQuality_RejectsUnknownValues()
    {
        Assert.Throws<InvalidOperationException>(() => RayTranslateRequest.NormalizeQuality("medium"));
    }

    [Fact]
    public void Build_IncludesSourceLanguageAndQuality()
    {
        var body = RayTranslateRequest.Build(
            subtitlePath: "/out/lingarr-tmp/sample.srt",
            sourceLanguage: "en",
            targetLanguage: "bg",
            quality: "fast",
            rayTarget: "local");

        Assert.Equal("fast", body["quality"]);
        Assert.Equal("srt", body["format"]);
        Assert.Equal("standard", body["coverage"]);
        Assert.Equal("local", body["target"]);
        Assert.Equal("translate", body["subtitle_source"]);
        Assert.Equal("en", body["source_language"]);
        Assert.Equal("bg", body["target_language"]);
        Assert.Equal("/out/lingarr-tmp/sample.srt", body["file_path"]);
        Assert.Equal("/out/lingarr-tmp/sample.srt", body["subtitle_path"]);
    }

    [Fact]
    public void Build_OmitsBlankSourceLanguage()
    {
        var body = RayTranslateRequest.Build(
            subtitlePath: "/out/lingarr-tmp/sample.srt",
            sourceLanguage: "  ",
            targetLanguage: "bg",
            quality: "best",
            rayTarget: "cloud");

        Assert.False(body.ContainsKey("source_language"));
        Assert.Equal("bg", body["target_language"]);
        Assert.Equal("cloud", body["target"]);
        Assert.Equal("best", body["quality"]);
    }
}
