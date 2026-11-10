using Lingarr.Plugin.Ray.DeepL;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class ProviderOrderTests
{
    [Theory]
    [InlineData(null, true, new[] { "deepl", "ray" })]
    [InlineData("", true, new[] { "deepl", "ray" })]
    [InlineData("  ", false, new[] { "ray" })]
    [InlineData("ray,deepl", true, new[] { "ray", "deepl" })]
    [InlineData("DeepL, Ray", true, new[] { "deepl", "ray" })]
    [InlineData("ray", true, new[] { "ray" })]
    [InlineData("deepl", true, new[] { "deepl" })]
    [InlineData("deepl,ray,deepl", true, new[] { "deepl", "ray" })]
    [InlineData("ray,deepl", false, new[] { "ray" })]
    [InlineData("deepl", false, new[] { "ray" })]
    [InlineData("openai,ray", true, new[] { "ray" })]
    public void Resolve_ParsesKnownProvidersAndAppliesDeepLAvailability(
        string? raw,
        bool deepLConfigured,
        string[] expected)
    {
        Assert.Equal(expected, ProviderOrder.Resolve(raw, deepLConfigured));
    }
}
