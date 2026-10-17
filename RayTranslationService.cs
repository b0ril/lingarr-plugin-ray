using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Settings;
using Lingarr.Contracts.Translation;
using Microsoft.Extensions.Logging;

namespace Lingarr.Plugin.Ray;

/// <summary>
/// Lingarr community plugin: batch-translates subtitle cues via Ray Headless (Sunlight).
/// Provider id: <c>ray</c>. Plugin API 1.0.
/// </summary>
[PluginProvider("ray")]
public sealed class RayTranslationService : ITranslationService, IBatchTranslationService, IDisposable
{
    private const int MaxCuesPerJob = 500;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsAccess _settings;
    private readonly ILogger<RayTranslationService> _logger;
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private string? _apiUrl;
    private string? _apiKey;
    private string _target = "local";
    private int _cuesPerJob = 200;
    private int _timeoutMinutes = 180;
    private bool _initialized;

    public RayTranslationService(ISettingsAccess settings, ILogger<RayTranslationService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string? ModelName => "Sunlight (Ray Local)";

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        var translated = await TranslateBatchAsync(
            [new BatchSubtitleItem { Position = 0, Line = text }],
            sourceLanguage,
            targetLanguage,
            cancellationToken);
        return translated[0];
    }

    public async Task<Dictionary<int, string>> TranslateBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var translations = new Dictionary<int, string>();

        foreach (var batch in subtitleBatch.Chunk(_cuesPerJob))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await TranslateSrtBatchAsync(batch, targetLanguage, cancellationToken);

            if (result.Count != batch.Length)
            {
                throw new TranslationException(
                    $"Ray returned {result.Count} subtitle cues for a batch of {batch.Length}.");
            }

            for (var index = 0; index < batch.Length; index++)
            {
                translations[batch[index].Position] = result[index];
            }
        }

        return translations;
    }

    public Task<List<SourceLanguage>> GetLanguages() =>
        Task.FromResult(new List<SourceLanguage>());

    public Task<ModelsResponse> GetModels() => Task.FromResult(new ModelsResponse
    {
        Options = [new LabelValue { Label = "Sunlight", Value = "best" }]
    });

    public Task<LanguagePair?> GetLanguagePair(
        string requestedSource,
        string requestedTarget,
        CancellationToken cancellationToken) =>
        Task.FromResult<LanguagePair?>(new LanguagePair
        {
            Source = requestedSource,
            Target = requestedTarget,
            Tier = MatchTier.Exact
        });

    public void Dispose()
    {
        _httpClient.Dispose();
        _initializeLock.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var endpoint = await _settings.GetSettingAsync("ray_endpoint");
            _apiKey = await _settings.GetEncryptedSettingAsync("ray_api_key");
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException(
                    "Configure the Ray endpoint and API key under Settings → Services → Ray Local.");
            }

            if (!Uri.TryCreate(endpoint.Trim().TrimEnd('/'), UriKind.Absolute, out var baseAddress)
                || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("The Ray endpoint must be an absolute HTTP or HTTPS URL.");
            }

            _apiUrl = baseAddress.ToString().TrimEnd('/');

            var sharedHttpSettings = await _settings.GetHttpSettingsAsync();
            _target = (await _settings.GetSettingAsync("ray_target"))?.Trim().ToLowerInvariant() ?? "local";
            if (_target is not ("local" or "cloud"))
            {
                throw new InvalidOperationException("Ray processing target must be 'local' or 'cloud'.");
            }

            _cuesPerJob = ParseBoundedSetting(
                await _settings.GetSettingAsync("ray_batch_size"), 200, 1, MaxCuesPerJob);
            _timeoutMinutes = ParseBoundedSetting(
                await _settings.GetSettingAsync("ray_job_timeout_minutes"), 180, 1, 360);

            _httpClient.Timeout = TimeSpan.FromMinutes(
                Math.Max(sharedHttpSettings.Timeout.TotalMinutes, _timeoutMinutes));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    // Shared with Ray Headless: Lingarr mounts full storage at /volume33,
    // Ray mounts ray-output at /out. Translate jobs require subtitle_path on a
    // path Ray can read (uploads alone return subtitle_required / file_path).
    private const string LingarrTempDir = "/volume33/ray-output/lingarr-tmp";
    private const string RayTempDir = "/out/lingarr-tmp";

    private async Task<List<string>> TranslateSrtBatchAsync(
        BatchSubtitleItem[] batch,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var fileName = $"lingarr-ray-{Guid.NewGuid():N}.srt";
        var srtBytes = Encoding.UTF8.GetBytes(CreateSrt(batch));
        Directory.CreateDirectory(LingarrTempDir);
        var lingarrPath = Path.Combine(LingarrTempDir, fileName);
        var rayPath = $"{RayTempDir}/{fileName}";
        await File.WriteAllBytesAsync(lingarrPath, srtBytes, cancellationToken);

        try
        {
            var jobId = await CreateJobAsync(rayPath, targetLanguage, cancellationToken);
            await WaitForCompletionAsync(jobId, cancellationToken);
            var translatedSrt = await DownloadResultAsync(jobId, cancellationToken);
            return ParseSrt(Encoding.UTF8.GetString(translatedSrt));
        }
        finally
        {
            try { File.Delete(lingarrPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not delete temporary Ray subtitle {Path}.", lingarrPath);
            }
        }
    }

    private async Task<string> CreateJobAsync(
        string subtitlePath,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        // Ray docs: translate with subtitle_source=translate + subtitle_path.
        // file_path must also be the subtitle path (media reference; not read for translate).
        var request = new Dictionary<string, object>
        {
            ["quality"] = "best",
            ["format"] = "srt",
            ["coverage"] = "standard",
            ["target"] = _target,
            ["subtitle_source"] = "translate",
            ["target_language"] = targetLanguage,
            ["file_path"] = subtitlePath,
            ["subtitle_path"] = subtitlePath
        };

        using var response = await SendJsonAsync(
            HttpMethod.Post,
            "/v1/subtitles",
            request,
            cancellationToken);
        var jobId = response.RootElement.GetProperty("job_id").GetString();
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new TranslationException("Ray did not return a job ID.");
        }

        _logger.LogInformation("Submitted subtitle batch {Path} to Ray as job {JobId}.",
            subtitlePath, jobId);
        return jobId;
    }

    private async Task WaitForCompletionAsync(string jobId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_timeoutMinutes));

        while (true)
        {
            using var jobsResponse = await SendJsonAsync(
                HttpMethod.Get,
                "/v1/jobs",
                null,
                timeout.Token);
            var jobs = jobsResponse.RootElement.GetProperty("jobs");
            var job = jobs.EnumerateArray().FirstOrDefault(candidate =>
                candidate.TryGetProperty("job_id", out var id)
                && id.GetString() == jobId);

            if (job.ValueKind == JsonValueKind.Undefined)
            {
                throw new TranslationException($"Ray job {jobId} disappeared before completing.");
            }

            var status = job.GetProperty("status").GetString();
            if (status == "completed")
            {
                return;
            }

            if (status is "failed" or "canceled")
            {
                var error = job.TryGetProperty("error", out var errorValue)
                    ? errorValue.GetString()
                    : null;
                throw new TranslationException($"Ray job {jobId} {status}: {error ?? "no details"}");
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }

    private async Task<byte[]> DownloadResultAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{_apiUrl}/v1/jobs/{Uri.EscapeDataString(jobId)}/result",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }


    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{_apiUrl}{path}");
        if (body is not null)
        {
            // JsonContent can use Transfer-Encoding: chunked. Ray Headless rejects that
            // (HTTP 411: "Transfer-Encoding is not supported; send Content-Length").
            var json = JsonSerializer.Serialize(body);
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
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

        throw new TranslationException(
            $"Ray API returned HTTP {(int)response.StatusCode}: {body}");
    }

    private static string CreateSrt(BatchSubtitleItem[] batch)
    {
        var output = new StringBuilder();
        for (var index = 0; index < batch.Length; index++)
        {
            var start = TimeSpan.FromSeconds(index * 5).ToString(@"hh\:mm\:ss\,fff");
            var end = TimeSpan.FromSeconds(index * 5 + 4).ToString(@"hh\:mm\:ss\,fff");
            var text = (batch[index].Line ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            output.AppendLine((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            output.Append(start).Append(" --> ").AppendLine(end);
            output.AppendLine(text);
            output.AppendLine();
        }

        return output.ToString();
    }

    private static List<string> ParseSrt(string content)
    {
        var normalized = content.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = Regex.Split(normalized.Trim(), @"\n\s*\n+");
        var cues = new List<string>(blocks.Length);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timingLine = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timingLine >= 0 && timingLine + 1 < lines.Length)
            {
                cues.Add(string.Join(" ", lines[(timingLine + 1)..]).Trim());
            }
        }

        return cues;
    }

    private static int ParseBoundedSetting(string? value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
}
