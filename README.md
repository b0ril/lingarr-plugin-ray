# Lingarr Plugin: Ray Local

Community plugin for [Lingarr](https://github.com/lingarr-translate/lingarr) that translates subtitle batches with **[Ray Headless](https://ray.app)** (Sunlight model).

| | |
|--|--|
| Provider id | `ray` |
| Plugin API | 1.0 (`LingarrPluginApiVersion`) |
| GitHub topic | [`lingarr-plugin`](https://github.com/topics/lingarr-plugin) |
| Maintainer | [b0ril](https://github.com/b0ril) |

Ray is closed-source and may require a license. This plugin is **not** part of Lingarr core; it is maintained separately.

## Build (DLL only)

Requires .NET 10 SDK and `Lingarr.Contracts.dll` from a matching Lingarr release (1.3+ / 1.4):

```bash
docker create --name lingarr-ref lingarr/lingarr:1.4.0
docker cp lingarr-ref:/app/Lingarr.Contracts.dll /tmp/Lingarr.Contracts.dll
docker rm lingarr-ref

dotnet publish RayLingarrPlugin.csproj -c Release -o ./out \
  -p:LingarrContractsPath=/tmp/Lingarr.Contracts.dll
```

Copy into your plugins folder:

- `Lingarr.Plugin.Ray.dll`
- `Lingarr.Contracts.dll` (same version Lingarr runs)

## Deploy (Compose volume)

```yaml
services:
  lingarr:
    image: lingarr/lingarr:1.4.0
    environment:
      PLUGINS_PATH: /app/plugins
    volumes:
      - ./plugins:/app/plugins
```

Restart Lingarr and look for:

```text
Loaded plugin /app/plugins/Lingarr.Plugin.Ray.dll (1 manifest(s))
```

Select **Ray Local** under **Settings → Services**.

## Deploy (custom image)

This repo’s `Dockerfile` builds stock Lingarr with the plugin baked into `/app/plugins`:

```bash
docker build -t lingarr-ray:1.4.0 \
  --build-arg LINGARR_BASE=lingarr/lingarr:1.4.0 .
```

Keep Watchtower **disabled** for that image tag, or a Hub pull can replace the container and drop `/app/plugins`.

## Settings

| UI label | Key | Notes |
|----------|-----|--------|
| Ray server URL | `ray_endpoint` | e.g. `http://127.0.0.1:8787` (host networking) |
| Ray API key | `ray_api_key` | Secret / encrypted |
| Ray processing target | `ray_target` | `local` (preferred) or `cloud` |
| Cues per Ray job | `ray_batch_size` | 1–500, default `200` |
| Ray job timeout (minutes) | `ray_job_timeout_minutes` | Default `180` |

Enable **batch translation** in Lingarr. Languages are accepted as requested (no remote language catalog).

## Behaviour notes

- Uploads temporary SRT cue batches; Ray translate jobs use `subtitle_upload_id` (not video `upload_id`).
- JSON requests send explicit `Content-Length` (Ray rejects chunked transfer encoding).
- Prefer `ray_target=local` for library translation so work stays on-device.

## License

MIT (this plugin). Ray itself is a third-party product; follow Ray’s terms and licensing.
