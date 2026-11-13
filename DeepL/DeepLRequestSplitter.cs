using System.Text;
using System.Text.Json;

namespace Lingarr.Plugin.Ray.DeepL;

/// <summary>
/// Splits subtitle cues into DeepL requests that stay under the 128 KiB body limit
/// and omits blank cues (DeepL rejects them with HTTP 400).
/// </summary>
public static class DeepLRequestSplitter
{
    public const int MaxPayloadBytes = 96 * 1024;

    public static List<int[]> Chunk(IReadOnlyList<string?> texts, int maxPayloadBytes = MaxPayloadBytes)
    {
        var chunks = new List<int[]>();
        var current = new List<int>();
        for (var index = 0; index < texts.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(texts[index]))
            {
                continue;
            }

            if (current.Count > 0 && PayloadBytes(texts, [.. current, index]) > maxPayloadBytes)
            {
                chunks.Add(current.ToArray());
                current.Clear();
            }

            current.Add(index);
        }

        if (current.Count > 0)
        {
            chunks.Add(current.ToArray());
        }

        return chunks;
    }

    public static int PayloadBytes(IReadOnlyList<string?> texts, IReadOnlyList<int> indices)
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = indices.Select(index => texts[index] ?? string.Empty).ToArray(),
            ["source_lang"] = "EN",
            ["target_lang"] = "BG"
        };
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload));
    }
}
