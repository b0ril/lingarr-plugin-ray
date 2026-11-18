using Lingarr.Plugin.Ray.DeepL;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class ProviderOrderTests
{
    [Fact]
    public void Resolve_UsesServicesListWhenNoOverrideIsSet()
    {
        var order = ProviderOrder.Resolve(
            explicitOrder: null,
            serviceType: """["ray","deepl"]""",
            deepLConfigured: true);

        Assert.Equal(["ray", "deepl"], order);
    }

    [Fact]
    public void Resolve_UsesExplicitOrderToAddLibreTranslateBeforeDeepL()
    {
        var order = ProviderOrder.Resolve(
            explicitOrder: "ray,libretranslate,deepl",
            serviceType: """["ray","deepl"]""",
            deepLConfigured: true);

        Assert.Equal(["ray", "libretranslate", "deepl"], order);
    }

    [Fact]
    public void WithCloudFallback_AddsRayCloudAfterConfiguredProvidersForLocalTarget()
    {
        var attempts = ProviderOrder.WithCloudFallback(
            ["ray", "libretranslate", "deepl"],
            "local");

        Assert.Equal(["ray", "libretranslate", "deepl", "ray-cloud"], attempts);
    }

    [Fact]
    public void WithCloudFallback_SkipsCloudWhenDisabled()
    {
        var attempts = ProviderOrder.WithCloudFallback(
            ["deepl", "ray"],
            "local",
            cloudFallbackEnabled: false);

        Assert.Equal(["deepl", "ray"], attempts);
    }

    [Fact]
    public void WithCloudFallback_DoesNotDuplicateExplicitCloudToken()
    {
        var attempts = ProviderOrder.WithCloudFallback(
            ["deepl", "ray", "ray-cloud"],
            "local",
            cloudFallbackEnabled: true);

        Assert.Equal(["deepl", "ray", "ray-cloud"], attempts);
    }

    [Fact]
    public void WithCloudFallback_DoesNotAddDuplicateCloudJobForCloudTarget()
    {
        var attempts = ProviderOrder.WithCloudFallback(
            ["ray", "deepl"],
            "cloud");

        Assert.Equal(["ray", "deepl"], attempts);
    }

    [Fact]
    public void WithCloudFallback_RecognizesLocalTargetWithoutCaseSensitivity()
    {
        var attempts = ProviderOrder.WithCloudFallback(
            ["ray", "deepl"],
            "LOCAL");

        Assert.Equal(["ray", "deepl", "ray-cloud"], attempts);
    }

    [Fact]
    public void WithCloudFallback_DoesNotAddCloudWhenRayIsNotConfigured()
    {
        var attempts = ProviderOrder.WithCloudFallback(
            ["deepl"],
            "local");

        Assert.Equal(["deepl"], attempts);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("false", true, false)]
    [InlineData("off", false, false)]
    [InlineData("true", false, true)]
    public void ParseCloudFallback_ReadsCommonTruthyFalsyTokens(
        string? raw,
        bool defaultValue,
        bool expected)
    {
        Assert.Equal(expected, ProviderOrder.ParseCloudFallback(raw, defaultValue));
    }

    [Fact]
    public void Resolve_UsesServicesListWhenDeepLIsPrimary()
    {
        var order = ProviderOrder.Resolve(
            explicitOrder: null,
            serviceType: """["deepl","ray"]""",
            deepLConfigured: true);

        Assert.Equal(["deepl", "ray"], order);
    }

    [Fact]
    public void Resolve_UsesExplicitOrderWhenDeepLIsNotInTheServicesList()
    {
        var order = ProviderOrder.Resolve(
            explicitOrder: "deepl,ray",
            serviceType: """["ray"]""",
            deepLConfigured: true);

        Assert.Equal(["deepl", "ray"], order);
    }

    [Theory]
    [InlineData("ray,deepl", true, new[] { "ray", "deepl" })]
    [InlineData("DeepL, Ray", true, new[] { "deepl", "ray" })]
    [InlineData("ray", true, new[] { "ray" })]
    [InlineData("deepl", true, new[] { "deepl" })]
    [InlineData("ray,deepl", false, new[] { "ray" })]
    [InlineData("deepl", false, new[] { "ray" })]
    public void Resolve_ParsesExplicitOrderWhenServiceListHasNoDeepL(
        string explicitOrder,
        bool deepLConfigured,
        string[] expected)
    {
        Assert.Equal(expected, ProviderOrder.Resolve(explicitOrder, serviceType: null, deepLConfigured));
    }
}
