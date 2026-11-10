using System.Text.Json;

namespace Lingarr.Plugin.Ray.DeepL;

/// <summary>
/// Order of <c>ray</c> / <c>deepl</c> for one subtitle file.
/// When Lingarr's service list includes DeepL, that list is the order
/// (Primary Ray + Fallback DeepL → <c>ray,deepl</c>). The DeepL API key
/// stays on the DeepL row (<c>deepl_api_key</c>).
/// <c>ray_provider_order</c> applies only when DeepL is not in the service list.
/// </summary>
public static class ProviderOrder
{
    public const string Ray = "ray";
    public const string DeepL = "deepl";

    public static IReadOnlyList<string> Resolve(
        string? explicitOrder,
        string? serviceType,
        bool deepLConfigured)
    {
        var fromServices = Parse(serviceType);
        var chosen = fromServices.Contains(DeepL)
            ? fromServices
            : Parse(explicitOrder);

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
            .Where(token => token is Ray or DeepL)
            .Distinct()
            .ToList();
    }
}
