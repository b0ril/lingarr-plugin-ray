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
      # Shared with Ray so temp batch SRTs are visible (see Behaviour)
      - /path/to/ray-output:/volume33/ray-output
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
| Ray processing target | `ray_target` | `local` or `cloud` (env: `RAY_TARGET`) |
| Ray job timeout (minutes) | `ray_job_timeout_minutes` | Default `180` |

Each Lingarr subtitle file becomes **one** Ray translate job (keep Lingarr `max_batch_size=0`).

Enable **batch translation** in Lingarr. Languages are accepted as requested (no remote language catalog).

## Behaviour notes

- **Content-Length:** JSON requests use `ByteArrayContent` (Ray rejects `Transfer-Encoding: chunked` with HTTP 411).
- **Path-based translate:** Ray’s `/v1/subtitles` translate API needs `subtitle_source=translate` plus **`file_path` and `subtitle_path`** on a path Ray can read. Bare SRT uploads alone return `subtitle_required` / `file_path is required`.
- The plugin writes each cue-batch SRT to **`/volume33/ray-output/lingarr-tmp/`** and submits paths as **`/out/lingarr-tmp/…`** (Ray’s usual `ray-output` → `/out` mount). Override those constants if your layout differs.
- **Quota:** with `ray_target=cloud`, each translate job can consume Ray Cloud’s **daily whole-video allowance** (same counter as create-from-video; resets midnight UTC). Prefer `local` when backfilling large libraries without burning Cloud admits.

## License

MIT (this plugin). Ray itself is a third-party product; follow Ray’s terms and licensing.
