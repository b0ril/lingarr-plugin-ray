namespace Lingarr.Plugin.Ray.DeepL;

public static class DeepLEndpoint
{
    public static string ResolveBaseUrl(string apiKey)
    {
        var key = apiKey.Trim();
        return key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? "https://api-free.deepl.com"
            : "https://api.deepl.com";
    }

    public static string NormalizeLanguageCode(string code) =>
        code.Trim().Replace('_', '-').ToUpperInvariant();
}
