using Lingarr.Plugin.Ray.DeepL;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class ProviderOrderTests
{
    [Fact]
    public void Resolve_UsesServicesListWhenDeepLIsAFallback()
    {
        var order = ProviderOrder.Resolve(
            explicitOrder: "deepl,ray",
            serviceType: """["ray","deepl"]""",
            deepLConfigured: true);

        Assert.Equal(["ray", "deepl"], order);
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
    public void Resolve_UsesExplicitOrderOnlyWhenDeepLIsNotInTheServicesList()
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
