namespace Lingarr.Plugin.Ray.DeepL;

/// <summary>
/// Parses <c>ray_provider_order</c> into an ordered list of <c>ray</c> / <c>deepl</c>.
/// Unknown tokens are ignored. DeepL entries are dropped when no API key is configured.
/// Empty/whitespace with a DeepL key defaults to <c>deepl,ray</c>; otherwise <c>ray</c>.
/// </summary>
public static class ProviderOrder
{
    public const string Ray = "ray";
    public const string DeepL = "deepl";

    public static IReadOnlyList<string> Resolve(string? raw, bool deepLConfigured)
    {
        var tokens = (raw ?? string.Empty)
            .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(token => token is Ray or DeepL)
            .Distinct()
            .ToList();

        if (tokens.Count == 0)
        {
            return deepLConfigured ? [DeepL, Ray] : [Ray];
        }

        if (!deepLConfigured)
        {
            tokens.RemoveAll(token => token == DeepL);
        }

        if (tokens.Count == 0)
        {
            return [Ray];
        }

        return tokens;
    }
}
