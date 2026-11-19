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
using Lingarr.Plugin.Ray.DeepL;
using Lingarr.Plugin.Ray.LibreTranslate;
using Microsoft.Extensions.Logging;

namespace Lingarr.Plugin.Ray;

/// <summary>
/// Lingarr community plugin: batch-translates subtitle cues via Ray Headless
/// (Sunlight/Moonlight via <c>ray_quality</c>). Optional LibreTranslate and DeepL
/// providers can be ordered with <c>ray_provider_order</c>. When Ray is local,
/// Ray Cloud may be appended after the configured providers if
/// <c>ray_cloud_fallback</c> is enabled (default true). DeepL quota uses a sticky
/// skip until usage recovers. Provider id: <c>ray</c>. Plugin API 1.0.
/// </summary>
[PluginProvider("ray")]
public sealed class RayTranslationService : ITranslationService, IBatchTranslationService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly DeepLQuotaGate DeepLQuota = new();

    private readonly ISettingsAccess _settings;
    private readonly ILogger<RayTranslationService> _logger;
    private readonly HttpClient _httpClient = new();
    private readonly HttpClient _deepLHttpClient = new();
    private readonly HttpClient _libreTranslateHttpClient = new();
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private string? _apiUrl;
    private string? _apiKey;
    private string? _libreTranslateEndpoint;
    private string? _deepLApiKey;
    private IReadOnlyList<string> _providerOrder = [ProviderOrder.Ray];
    private string _target = "local";
    private string _quality = "best";
    private bool _cloudFallbackEnabled = true;
    private int _timeoutMinutes = 180;
    private bool _initialized;

    public RayTranslationService(ISettingsAccess settings, ILogger<RayTranslationService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string? ModelName
    {
        get
        {
            var qualityLabel = _quality == "fast" ? "Moonlight" : "Sunlight";
            return string.IsNullOrWhiteSpace(_deepLApiKey)
                ? $"{qualityLabel} (Ray Local)"
                : $"DeepL + {qualityLabel} (Ray)";
        }
    }

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
        await RefreshChainAsync(cancellationToken);

        var batch = subtitleBatch.ToArray();
        Exception? lastError = null;

        var attemptOrder = ProviderOrder.WithCloudFallback(
            _providerOrder, _target, _cloudFallbackEnabled);
        foreach (var provider in attemptOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (provider == ProviderOrder.RayCloud)
            {
                _logger.LogInformation(
                    "Configured providers failed; trying Ray Cloud as the final fallback.");
            }

            try
            {
                Dictionary<int, string>? result = provider switch
                {
                    ProviderOrder.DeepL => await TryTranslateWithDeepLAsync(
                        batch, sourceLanguage, targetLanguage, cancellationToken),
                    ProviderOrder.LibreTranslate => await TranslateWithLibreTranslateAsync(
                        batch, sourceLanguage, targetLanguage, cancellationToken),
                    ProviderOrder.Ray => await TranslateWithRayAsync(
                        batch, sourceLanguage, targetLanguage, _target, cancellationToken),
                    ProviderOrder.RayCloud => await TranslateWithRayAsync(
                        batch, sourceLanguage, targetLanguage, "cloud", cancellationToken),
                    _ => null
                };

                if (result is not null)
                {
                    return result;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (provider is
                ProviderOrder.Ray or ProviderOrder.RayCloud or ProviderOrder.LibreTranslate)
            {
                lastError = ex;
                _logger.LogWarning(
                    ex,
                    "{Provider} translation failed; trying next provider in order.",
                    ProviderName(provider));
            }
        }

        var attemptedProviders = attemptOrder.Select(provider => provider switch
        {
            ProviderOrder.Ray when _target == "local" => "Ray Local",
            ProviderOrder.Ray => "Ray Cloud",
            ProviderOrder.LibreTranslate => "LibreTranslate",
            ProviderOrder.DeepL => "DeepL",
            ProviderOrder.RayCloud => "Ray Cloud",
            _ => provider
        });
        throw new TranslationException(
            $"All configured providers failed ({string.Join(" → ", attemptedProviders)}).",
            lastError);
    }

    private async Task<Dictionary<int, string>> TranslateWithRayAsync(
        BatchSubtitleItem[] batch,
        string sourceLanguage,
        string targetLanguage,
        string rayTarget,
        CancellationToken cancellationToken)
    {
        // One Lingarr subtitle file (batch) → one Ray translate job.
        // Lingarr already sends the full SRT when max_batch_size is 0.
        var result = await TranslateSrtBatchAsync(
            batch, sourceLanguage, targetLanguage, rayTarget, cancellationToken);
        if (result.Count != batch.Length)
        {
            throw new TranslationException(
                $"Ray returned {result.Count} subtitle cues for a batch of {batch.Length}.");
        }

        var translations = new Dictionary<int, string>(batch.Length);
        for (var index = 0; index < batch.Length; index++)
        {
            translations[batch[index].Position] = result[index];
        }

        return translations;
    }

    public Task<List<SourceLanguage>> GetLanguages() =>
        Task.FromResult(new List<SourceLanguage>());

    public Task<ModelsResponse> GetModels() => Task.FromResult(new ModelsResponse
    {
        Options =
        [
            new LabelValue { Label = "Sunlight", Value = "best" },
            new LabelValue { Label = "Moonlight", Value = "fast" }
        ]
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
        _deepLHttpClient.Dispose();
        _libreTranslateHttpClient.Dispose();
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
            var libreTranslateEndpoint = await _settings.GetSettingAsync("ray_libretranslate_endpoint");
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

            _libreTranslateEndpoint = string.IsNullOrWhiteSpace(libreTranslateEndpoint)
                ? "http://127.0.0.1:5001"
                : ResolveHttpEndpoint(libreTranslateEndpoint, "LibreTranslate");

            var sharedHttpSettings = await _settings.GetHttpSettingsAsync();
            _target = (await _settings.GetSettingAsync("ray_target"))?.Trim().ToLowerInvariant() ?? "local";
            if (_target is not ("local" or "cloud"))
            {
                throw new InvalidOperationException("Ray processing target must be 'local' or 'cloud'.");
            }

            _quality = RayTranslateRequest.NormalizeQuality(
                await _settings.GetSettingAsync("ray_quality"));

            // Cloud jobs may sit Queued under allowance_wait until midnight UTC.
            // Cap high enough to outlast a full daily reset wait (not Hangfire's default 3h).
            _timeoutMinutes = ParseBoundedSetting(
                await _settings.GetSettingAsync("ray_job_timeout_minutes"), 1440, 1, 2880);

            _httpClient.Timeout = TimeSpan.FromMinutes(
                Math.Max(sharedHttpSettings.Timeout.TotalMinutes, _timeoutMinutes));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            _deepLHttpClient.Timeout = sharedHttpSettings.Timeout;
            _libreTranslateHttpClient.Timeout = sharedHttpSettings.Timeout;

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    /// <summary>
    /// Re-read each batch so the Services list and provider settings apply without a restart.
    /// Plugin fields <c>ray_deepl_api_key</c> / <c>ray_provider_order</c> are optional overrides.
    /// </summary>
    private async Task RefreshChainAsync(CancellationToken cancellationToken)
    {
        var pluginKey = await _settings.GetEncryptedSettingAsync("ray_deepl_api_key");
        var builtInKey = await _settings.GetEncryptedSettingAsync("deepl_api_key");
        _deepLApiKey = FirstNonEmpty(pluginKey, builtInKey);

        var previous = string.Join(",", _providerOrder);
        var previousCloud = _cloudFallbackEnabled;
        var previousQuality = _quality;
        _providerOrder = ProviderOrder.Resolve(
            await _settings.GetSettingAsync("ray_provider_order"),
            await _settings.GetSettingAsync("service_type"),
            deepLConfigured: _deepLApiKey is not null);
        _cloudFallbackEnabled = ProviderOrder.ParseCloudFallback(
            await _settings.GetSettingAsync("ray_cloud_fallback"),
            defaultValue: true);
        _quality = RayTranslateRequest.NormalizeQuality(
            await _settings.GetSettingAsync("ray_quality"));

        var next = string.Join(",", _providerOrder);
        if (!string.Equals(previous, next, StringComparison.Ordinal)
            || previousCloud != _cloudFallbackEnabled
            || !string.Equals(previousQuality, _quality, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Provider order: {Order} (DeepL key: {Source}; Cloud fallback: {CloudFallback}; quality: {Quality}).",
                string.Join(" → ", _providerOrder),
                _deepLApiKey is null
                    ? "missing"
                    : string.IsNullOrWhiteSpace(pluginKey) ? "DeepL service" : "Ray Local override",
                _cloudFallbackEnabled,
                _quality);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string ResolveHttpEndpoint(string endpoint, string name)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"{name} endpoint must be an absolute HTTP or HTTPS URL.");
        }

        return baseAddress.ToString().TrimEnd('/');
    }

    private string ProviderName(string provider) => provider switch
    {
        ProviderOrder.Ray when _target == "local" => "Ray Local",
        ProviderOrder.Ray => "Ray Cloud",
        ProviderOrder.RayCloud => "Ray Cloud",
        ProviderOrder.LibreTranslate => "LibreTranslate",
        ProviderOrder.DeepL => "DeepL",
        _ => provider
    };

    private async Task<Dictionary<int, string>> TranslateWithLibreTranslateAsync(
        BatchSubtitleItem[] batch,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var client = new LibreTranslateClient(
            _libreTranslateHttpClient,
            _libreTranslateEndpoint ?? "http://127.0.0.1:5001");
        var texts = batch.Select(item => item.Line ?? string.Empty).ToList();
        var translated = await client.TranslateAsync(
            texts,
            sourceLanguage,
            targetLanguage,
            cancellationToken);

        var translations = new Dictionary<int, string>(batch.Length);
        for (var index = 0; index < batch.Length; index++)
        {
            translations[batch[index].Position] = translated[index];
        }

        _logger.LogInformation(
            "Translated {Count} subtitle cues with LibreTranslate ({Source}->{Target}).",
            batch.Length, sourceLanguage, targetLanguage);
        return translations;
    }

    private async Task<Dictionary<int, string>?> TryTranslateWithDeepLAsync(
        BatchSubtitleItem[] batch,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_deepLApiKey))
        {
            return null;
        }

        var client = new DeepLClient(
            _deepLHttpClient,
            DeepLEndpoint.ResolveBaseUrl(_deepLApiKey),
            _deepLApiKey);

        try
        {
            if (DeepLQuota.IsExhausted)
            {
                var usageWhileSkipped = await client.GetUsageAsync(cancellationToken);
                DeepLQuota.RefreshFromUsage(usageWhileSkipped.AnyLimitReached);
                if (DeepLQuota.IsExhausted)
                {
                    _logger.LogInformation(
                        "DeepL still over quota ({Count}/{Limit}); skipping it for this file.",
                        usageWhileSkipped.CharacterCount,
                        usageWhileSkipped.CharacterLimit);
                    return null;
                }

                _logger.LogInformation("DeepL quota recovered; using DeepL again.");
            }
            else
            {
                var usage = await client.GetUsageAsync(cancellationToken);
                if (usage.AnyLimitReached)
                {
                    DeepLQuota.MarkExhausted();
                    _logger.LogWarning(
                        "DeepL usage limit reached ({Count}/{Limit}); skipping it for this file.",
                        usage.CharacterCount,
                        usage.CharacterLimit);
                    return null;
                }
            }

            var source = DeepLEndpoint.NormalizeLanguageCode(sourceLanguage);
            var target = DeepLEndpoint.NormalizeLanguageCode(targetLanguage);
            var texts = batch.Select(item => item.Line ?? string.Empty).ToList();
            var translated = await client.TranslateAsync(texts, source, target, cancellationToken);

            var translations = new Dictionary<int, string>(batch.Length);
            for (var index = 0; index < batch.Length; index++)
            {
                translations[batch[index].Position] = translated[index];
            }

            _logger.LogInformation(
                "Translated {Count} subtitle cues with DeepL ({Source}->{Target}).",
                batch.Length, source, target);
            return translations;
        }
        catch (DeepLQuotaException ex)
        {
            DeepLQuota.MarkExhausted();
            _logger.LogWarning(ex, "DeepL quota exceeded; skipping it for this file.");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeepL translation failed for this file.");
            return null;
        }
    }

    // Shared with Ray Headless: Lingarr mounts full storage at /volume33,
    // Ray mounts ray-output at /out. Translate jobs require subtitle_path on a
    // path Ray can read (uploads alone return subtitle_required / file_path).
    private const string LingarrTempDir = "/volume33/ray-output/lingarr-tmp";
    private const string RayTempDir = "/out/lingarr-tmp";

    private async Task<List<string>> TranslateSrtBatchAsync(
        BatchSubtitleItem[] batch,
        string sourceLanguage,
        string targetLanguage,
        string rayTarget,
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
            var jobId = await CreateJobAsync(
                rayPath, sourceLanguage, targetLanguage, rayTarget, cancellationToken);
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
        string sourceLanguage,
        string targetLanguage,
        string rayTarget,
        CancellationToken cancellationToken)
    {
        // Ray docs: translate with subtitle_source=translate + subtitle_path.
        // file_path must also be the subtitle path (media reference; not read for translate).
        // Pass source_language when Lingarr knows it — avoids local_subtitle_language_unknown.
        var request = RayTranslateRequest.Build(
            subtitlePath,
            sourceLanguage,
            targetLanguage,
            _quality,
            rayTarget);

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

        _logger.LogInformation(
            "Submitted subtitle batch {Path} to Ray ({Target}, quality={Quality}, source={Source}) as job {JobId}.",
            subtitlePath,
            rayTarget,
            _quality,
            string.IsNullOrWhiteSpace(sourceLanguage) ? "(detect)" : sourceLanguage.Trim(),
            jobId);
        return jobId;
    }

    private async Task WaitForCompletionAsync(string jobId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_timeoutMinutes));

        while (true)
        {
            JsonDocument jobsResponse;
            try
            {
                jobsResponse = await SendJsonAsync(
                    HttpMethod.Get,
                    "/v1/jobs",
                    null,
                    timeout.Token);
            }
            catch (Exception ex) when (IsTransient(ex) && !timeout.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Ray job list request dropped; retrying {JobId}.", jobId);
                await Task.Delay(PollInterval, timeout.Token);
                continue;
            }

            using (jobsResponse)
            {
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
    }

    private async Task<byte[]> DownloadResultAsync(string jobId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    $"{_apiUrl}/v1/jobs/{Uri.EscapeDataString(jobId)}/result",
                    cancellationToken);
                await EnsureSuccessAsync(response, cancellationToken);
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < 5 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Ray result download dropped for {JobId}; retry {Attempt}.", jobId, attempt);
                await Task.Delay(PollInterval * attempt, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException or JsonException;


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
