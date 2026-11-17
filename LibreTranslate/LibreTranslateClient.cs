using System.Text;
using System.Text.Json;

namespace Lingarr.Plugin.Ray.LibreTranslate;

public sealed class LibreTranslateClient
{
    private const int MaxPayloadBytes = 64 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public LibreTranslateClient(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var source = NormalizeLanguageCode(sourceLanguage);
        var target = NormalizeLanguageCode(targetLanguage);
        var results = texts.Select(text => text ?? string.Empty).ToArray();

        foreach (var chunk in Chunk(texts, source, target))
        {
            var translated = await TranslateChunkAsync(
                chunk.Select(index => texts[index] ?? string.Empty).ToArray(),
                source,
                target,
                cancellationToken);
            for (var index = 0; index < chunk.Length; index++)
            {
                results[chunk[index]] = translated[index];
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<string>> TranslateChunkAsync(
        string[] texts,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["q"] = texts,
            ["source"] = sourceLanguage,
            ["target"] = targetLanguage,
            ["format"] = "text"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/translate")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 1000)
            {
                body = body[..1000];
            }

            throw new InvalidOperationException(
                $"LibreTranslate returned HTTP {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("translatedText", out var translations)
            || translations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "LibreTranslate response did not include a translatedText array.");
        }

        var results = new List<string>(texts.Length);
        foreach (var item in translations.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text)
            {
                throw new InvalidOperationException(
                    "LibreTranslate response included a non-text translation.");
            }

            results.Add(text);
        }

        if (results.Count != texts.Length)
        {
            throw new InvalidOperationException(
                $"LibreTranslate returned {results.Count} translations for {texts.Length} texts.");
        }

        return results;
    }

    private static List<int[]> Chunk(
        IReadOnlyList<string> texts,
        string sourceLanguage,
        string targetLanguage)
    {
        var emptyPayload = SerializePayload([], sourceLanguage, targetLanguage);
        var baseBytes = Encoding.UTF8.GetByteCount(emptyPayload);
        var chunks = new List<int[]>();
        var current = new List<int>();
        var currentTextBytes = 0;

        for (var index = 0; index < texts.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(texts[index]))
            {
                continue;
            }

            var encodedTextBytes = Encoding.UTF8.GetByteCount(
                JsonSerializer.Serialize(texts[index]));
            var requiredBytes = baseBytes + currentTextBytes + encodedTextBytes
                + (current.Count > 0 ? current.Count : 0);

            if (current.Count > 0 && requiredBytes > MaxPayloadBytes)
            {
                chunks.Add(current.ToArray());
                current.Clear();
                currentTextBytes = 0;
            }

            current.Add(index);
            currentTextBytes += encodedTextBytes;
            if (current.Count > 1)
            {
                currentTextBytes++;
            }
        }

        if (current.Count > 0)
        {
            chunks.Add(current.ToArray());
        }

        return chunks;
    }

    private static string SerializePayload(
        string[] texts,
        string sourceLanguage,
        string targetLanguage) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["q"] = texts,
            ["source"] = sourceLanguage,
            ["target"] = targetLanguage,
            ["format"] = "text"
        });

    private static string NormalizeLanguageCode(string code) =>
        code.Trim().Replace('_', '-').Split('-', 2)[0].ToLowerInvariant();
}
