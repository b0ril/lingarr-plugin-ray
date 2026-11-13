using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lingarr.Plugin.Ray.DeepL;

public sealed class DeepLClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public DeepLClient(HttpClient httpClient, string baseUrl, string apiKey)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey.Trim();
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

        var results = texts.Select(text => text ?? string.Empty).ToArray();
        foreach (var chunk in DeepLRequestSplitter.Chunk(texts))
        {
            var translated = await TranslateChunkAsync(
                chunk.Select(index => texts[index] ?? string.Empty).ToArray(),
                sourceLanguage,
                targetLanguage,
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
            ["text"] = texts,
            ["source_lang"] = sourceLanguage,
            ["target_lang"] = targetLanguage
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/translate")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("translations", out var translations)
            || translations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("DeepL translate response did not include translations.");
        }

        var results = new List<string>(texts.Length);
        foreach (var item in translations.EnumerateArray())
        {
            results.Add(item.GetProperty("text").GetString() ?? string.Empty);
        }

        if (results.Count != texts.Length)
        {
            throw new InvalidOperationException(
                $"DeepL returned {results.Count} translations for {texts.Length} texts.");
        }

        return results;
    }

    public async Task<DeepLUsage> GetUsageAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v2/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var count = root.GetProperty("character_count").GetInt64();
        var limit = root.GetProperty("character_limit").GetInt64();
        return new DeepLUsage(count, limit);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 1000)
        {
            body = body[..1000];
        }

        if ((int)response.StatusCode == 456)
        {
            throw new DeepLQuotaException($"DeepL quota exceeded (HTTP 456): {body}");
        }

        throw new InvalidOperationException(
            $"DeepL API returned HTTP {(int)response.StatusCode}: {body}");
    }
}
