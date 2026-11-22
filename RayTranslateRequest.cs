namespace Lingarr.Plugin.Ray;

/// <summary>
/// Builds the JSON body for Ray Headless <c>POST /v1/subtitles</c> translate jobs.
/// </summary>
public static class RayTranslateRequest
{
    public static string NormalizeQuality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "best";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "best" or "sunlight" => "best",
            "fast" or "moonlight" => "fast",
            _ => throw new InvalidOperationException(
                "Ray quality must be 'best'/'sunlight' or 'fast'/'moonlight'.")
        };
    }

    public static Dictionary<string, object> Build(
        string subtitlePath,
        string? sourceLanguage,
        string targetLanguage,
        string quality,
        string rayTarget)
    {
        // Do not send source_language on subtitle_source=translate.
        // Ray returns HTTP 400 source_language_not_used unless the job is
        // automatic / generate_from_audio. sourceLanguage is kept on the
        // signature for DeepL and call-site compatibility only.
        _ = sourceLanguage;
        return new Dictionary<string, object>
        {
            ["quality"] = NormalizeQuality(quality),
            ["format"] = "srt",
            ["coverage"] = "standard",
            ["target"] = rayTarget,
            ["subtitle_source"] = "translate",
            ["target_language"] = targetLanguage,
            ["file_path"] = subtitlePath,
            ["subtitle_path"] = subtitlePath
        };
    }
}
