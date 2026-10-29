using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Plugin.Ray;

public sealed class RayPluginManifest : IPluginManifest
{
    public string Provider => "ray";

    public string DisplayName => "Ray Local";

    public string? Description =>
        "Community plugin: translates subtitle batches with a Ray Headless server (Sunlight). Use target local when the host can run Sunlight reliably; use cloud for Ray Cloud (shares the daily whole-video allowance with create-from-video).";

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
