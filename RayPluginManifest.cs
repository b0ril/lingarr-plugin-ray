using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.Ray;

public sealed class RayPluginManifest : IPluginManifest
{
    public string Provider => "ray";

    public string DisplayName => "Ray Local";

    public string? Description =>
        "Community plugin: translates subtitle batches with Ray Headless (Sunlight) and optional DeepL. Set ray_provider_order to deepl,ray or ray,deepl (or a single provider). Keep Lingarr SERVICE_TYPE on ray only — do not also chain built-in DeepL.";

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
            Key = "ray_deepl_api_key",
            Label = "DeepL API key (optional)",
            Type = PluginSettingType.Secret,
            Required = false,
            Description = "DeepL Auth Key (Pro → api.deepl.com, Free :fx → api-free.deepl.com). Required for any order that includes deepl. Quota exhaustion sticky-skips DeepL until /v2/usage recovers."
        },
        new()
        {
            Key = "ray_provider_order",
            Label = "Provider order",
            Type = PluginSettingType.Text,
            Required = false,
            Default = "deepl,ray",
            Description = "Comma-separated providers to try for each subtitle file: deepl and/or ray. Examples: deepl,ray (DeepL first) · ray,deepl (Ray first) · ray · deepl. Empty with a DeepL key defaults to deepl,ray. Keep Lingarr services list as Ray Local only."
        },
        new()
        {
            Key = "ray_target",
            Label = "Ray processing target",
            Type = PluginSettingType.Text,
            Required = true,
            Default = "local",
            Description = "local = on-device Sunlight; cloud = Ray Cloud. Cloud translate shares the daily whole-video admit cap with create-from-video."
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
