using System.Text.Json;

namespace Lingarr.Plugin.Ray.DeepL;

/// <summary>
/// Order of configured providers for one subtitle file.
/// When set, <c>ray_provider_order</c> overrides Lingarr's Services-list order.
/// The DeepL API key stays on the DeepL row (<c>deepl_api_key</c>).
/// </summary>
public static class ProviderOrder
{
    public const string Ray = "ray";
    public const string RayCloud = "ray-cloud";
    public const string LibreTranslate = "libretranslate";
    public const string DeepL = "deepl";

    public static IReadOnlyList<string> WithCloudFallback(
        IReadOnlyList<string> order,
        string rayTarget)
    {
        if (!string.Equals(rayTarget, "local", StringComparison.OrdinalIgnoreCase)
            || !order.Contains(Ray, StringComparer.Ordinal))
        {
            return order;
        }

        return [.. order, RayCloud];
    }

    public static IReadOnlyList<string> Resolve(
        string? explicitOrder,
        string? serviceType,
        bool deepLConfigured)
    {
        var fromServices = Parse(serviceType);
        var fromOverride = Parse(explicitOrder);
        var chosen = fromOverride.Count > 0 ? fromOverride : fromServices;

        if (chosen.Count == 0)
        {
            chosen = fromServices.Count > 0 ? fromServices : [Ray];
        }

        if (!deepLConfigured)
        {
            chosen = chosen.Where(token => token != DeepL).ToList();
        }

        if (chosen.Count == 0)
        {
            return [Ray];
        }

        return chosen;
    }

    public static List<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var trimmed = raw.Trim();
        IEnumerable<string> tokens;
        if (trimmed.StartsWith('['))
        {
            try
            {
                tokens = JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        else
        {
            tokens = trimmed.Split(
                [',', ';', '|', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return tokens
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token is Ray or LibreTranslate or DeepL)
            .Distinct()
            .ToList();
    }
}
