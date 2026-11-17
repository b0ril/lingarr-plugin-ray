using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.Ray;

public sealed class RayPluginManifest : IPluginManifest
{
    public string Provider => "ray";

    public string DisplayName => "Ray Local";

    public string? Description =>
        "Community plugin: batch-translates with Ray Headless (Sunlight). Optional LibreTranslate and DeepL providers can be ordered with Provider order override; the DeepL key stays on the DeepL service row. With Ray Local selected, Ray Cloud is the final fallback if configured providers fail.";

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = "ray_endpoint",
            Label = "Ray server URL",
            Type = PluginSettingType.Url,
            Required = true,
            Default = "http://127.0.0.1:8787",
            Description = "Base URL of the Ray Headless API (for example http://127.0.0.1:8787 when using host networking)."
        },
        new()
        {
            Key = "ray_api_key",
            Label = "Ray API key",
            Type = PluginSettingType.Secret,
            Required = true,
            Description = "Ray server API key. Stored encrypted by Lingarr.",
            MinLength = 1,
            ValidationErrorMessage = "Value must not be empty"
        },
        new()
        {
            Key = "ray_libretranslate_endpoint",
            Label = "LibreTranslate URL (optional)",
            Type = PluginSettingType.Url,
            Required = false,
            Default = "http://127.0.0.1:5001",
            Description = "Base URL of LibreTranslate when selected in Provider order. Host networking uses http://127.0.0.1:5001."
        },
        new()
        {
            Key = "ray_deepl_api_key",
            Label = "DeepL API key override (optional)",
            Type = PluginSettingType.Secret,
            Required = false,
            Description = "Leave empty. The key on the DeepL service row (deepl_api_key) is used. Fill this only to override that key. Pro keys use api.deepl.com; Free keys ending in :fx use api-free.deepl.com."
        },
        new()
        {
            Key = "ray_provider_order",
            Label = "Provider order override (optional)",
            Type = PluginSettingType.Text,
            Required = false,
            Description = "Optional comma-separated order, for example ray,libretranslate,deepl. When set, this overrides the Services-list order. DeepL must be configured on its service row."
        },
        new()
        {
            Key = "ray_target",
            Label = "Ray processing target",
            Type = PluginSettingType.Text,
            Required = true,
            Default = "local",
            Description = "local = Ray Local, then configured providers (e.g. DeepL), then Ray Cloud if none succeeds; cloud = Ray Cloud directly. Cloud translate shares the daily whole-video admit cap with create-from-video."
        },
        new()
        {
            Key = "ray_job_timeout_minutes",
            Label = "Ray job timeout (minutes)",
            Type = PluginSettingType.Text,
            Required = true,
            Default = "1440",
            Description = "Maximum wait for each Ray translation job (one job per subtitle file). With ray_target=cloud, Ray may keep jobs Queued until the daily whole-video allowance resets (midnight UTC). Use ≥1440 so Lingarr does not mark Failed while Ray is only paused."
        }
    ];
}
