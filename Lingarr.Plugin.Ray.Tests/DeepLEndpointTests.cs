using Lingarr.Plugin.Ray.DeepL;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class DeepLEndpointTests
{
    [Theory]
    [InlineData("abc:fx", "https://api-free.deepl.com")]
    [InlineData("ABC:FX", "https://api-free.deepl.com")]
    [InlineData("pro-key-without-suffix", "https://api.deepl.com")]
    [InlineData("  pro-key  ", "https://api.deepl.com")]
    public void ResolveBaseUrl_SelectsHostFromKey(string apiKey, string expected)
    {
        Assert.Equal(expected, DeepLEndpoint.ResolveBaseUrl(apiKey));
    }

    [Theory]
    [InlineData("en", "EN")]
    [InlineData("bg", "BG")]
    [InlineData("en-US", "EN-US")]
    [InlineData("pt_BR", "PT-BR")]
    public void NormalizeLanguageCode_UppercasesAndUsesHyphen(string input, string expected)
    {
        Assert.Equal(expected, DeepLEndpoint.NormalizeLanguageCode(input));
    }
}
