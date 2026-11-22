# Lingarr Plugin: Ray Local

Community plugin for [Lingarr](https://github.com/lingarr-translate/lingarr) that translates subtitle batches with **[Ray Headless](https://ray.app)** (Sunlight / Moonlight).

| | |
|--|--|
| Provider id | `ray` |
| Assembly | `Lingarr.Plugin.Ray` |
| Version | **1.2.7** |
| Plugin API | 1.0 (`LingarrPluginApiVersion`) |
| GitHub topic | [`lingarr-plugin`](https://github.com/topics/lingarr-plugin) |
| Maintainer | [b0ril](https://github.com/b0ril) |

Ray is closed-source and may require a license. This plugin is **not** part of Lingarr core; it is maintained separately.

See [LESSONS-LEARNED.md](LESSONS-LEARNED.md) for upstream Ray bugs (`local_stalled`, language detection) and ops notes.

## Build (DLL only)

Requires .NET 10 SDK and `Lingarr.Contracts.dll` from a matching Lingarr release (1.5.x):

```bash
docker create --name lingarr-ref lingarr/lingarr:1.5.0
docker cp lingarr-ref:/app/Lingarr.Contracts.dll /tmp/Lingarr.Contracts.dll
docker rm lingarr-ref

dotnet publish RayLingarrPlugin.csproj -c Release -o ./out \
  -p:LingarrContractsPath=/tmp/Lingarr.Contracts.dll

dotnet test Lingarr.Plugin.Ray.Tests/Lingarr.Plugin.Ray.Tests.csproj \
  -p:LingarrContractsPath=/tmp/Lingarr.Contracts.dll
```

Install the DLL into your plugins folder:

- `Lingarr.Plugin.Ray.dll` (**1.2.7**: omit `source_language` on translate; `ray_quality` best/fast)

## Deploy (Compose volume)

```yaml
services:
  lingarr:
    image: lingarr/lingarr:1.5.0
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

Under **Settings → Services** (Lingarr 1.5.0+), keep **DeepL** above **Ray Local** in the list. Put the API key on the **DeepL** instance. Leave the Ray Local DeepL override empty. Optional `ray_provider_order=deepl,ray` still overrides the UI cascade for this plugin’s batch path.

## Deploy (custom image)

This repo’s `Dockerfile` builds stock Lingarr with the plugin baked into `/app/plugins`:

```bash
docker build -t lingarr-ray:1.5.0 \
  --build-arg LINGARR_BASE=lingarr/lingarr:1.5.0 .
```

Prefer the volume-mounted DLL model on production hosts. If you bake an image, keep Watchtower **disabled** for that tag, or a Hub pull can replace the container and drop `/app/plugins`.

Host installs (this maintainer’s stack): `DOCKER_CONTEXT=newnas` + multimedia `newnas/scripts/install-lingarr-ray-plugin.sh` (pins a git SHA of this repo; override with `PLUGIN_REPO=/path/to/checkout` for local builds).

## Settings

| UI label | Key | Notes |
|----------|-----|--------|
| Ray server URL | `ray_endpoint` | e.g. `http://127.0.0.1:8787` (host networking) |
| Ray API key | `ray_api_key` | Secret / encrypted |
| LibreTranslate URL (optional) | `ray_libretranslate_endpoint` | Default `http://127.0.0.1:5001`; used only when LibreTranslate is in the provider order. |
| DeepL API key (optional override) | `ray_deepl_api_key` | Leave empty. The key on the **DeepL** service row is used. |
| Provider order override | `ray_provider_order` | Optional comma-separated order, e.g. `deepl,ray` or `ray,libretranslate,deepl`. When set, overrides the Services list. |
| Ray processing target | `ray_target` | `local` (default) or `cloud`. Compose `RAY_TARGET` is a hint only on stock Lingarr. |
| Ray quality | `ray_quality` | `best`/`sunlight` (default) or `fast`/`moonlight`. Until Ray fixes Local progress reporting, Moonlight is safer for long Local files (see Known Ray issues). |
| Ray Cloud fallback after Local | `ray_cloud_fallback` | `true` (default) appends Ray Cloud after configured providers when `ray_target=local`; `false` keeps Lingarr on the configured list only (use when Cloud admits are reserved for manual create / scheduled spillover). |
| Ray job timeout (minutes) | `ray_job_timeout_minutes` | Default **`1440`** (clamped 1–2880). Must outlast Cloud `allowance_wait` until midnight UTC. |

Each Lingarr subtitle file becomes **one** Ray translate job when Ray is used (keep Lingarr `max_batch_size=0`).

Enable **batch translation** in Lingarr. Languages are accepted as requested (no remote language catalog).

### Provider order

Without an override, the plugin follows the **Services** list. Set `ray_provider_order` under Ray Local to include LibreTranslate or override that order:

| Services list / override | What runs |
|---------------|-----------|
| Ray then DeepL; override empty; cloud fallback on | Ray Local → DeepL → Ray Cloud |
| Override `deepl,ray`; `ray_cloud_fallback=false` | DeepL → Ray Local (no Cloud) |
| Override `deepl,ray`; cloud fallback on | DeepL → Ray Local → Ray Cloud |
| Override `ray,libretranslate,deepl` | Ray Local → LibreTranslate → DeepL → (+ Cloud if fallback on) |

Put the DeepL API key on the **DeepL** row. Leave **DeepL API key override** on Ray Local empty. LibreTranslate needs no API key; batches are split under 64 KiB and blank cues are preserved but not sent.

Batch mode does not call built-in DeepL itself. This plugin reads the Services list, `ray_provider_order`, and the DeepL service-row key to run the configured chain. Ray Cloud fallback occurs only after the configured provider chain fails. DeepL calls are split under 96 KiB and blank cues are skipped. A dropped Ray poll or result download is retried. DeepL quota (HTTP 456 / usage limit) sticky-skips DeepL until usage recovers. Failed rows already on the Translations page stay Failed until Retry.

## Behaviour notes

- **Content-Length:** JSON requests use `ByteArrayContent` (Ray rejects `Transfer-Encoding: chunked` with HTTP 411).
- **Path-based translate:** Ray’s `/v1/subtitles` translate API needs `subtitle_source=translate` plus **`file_path` and `subtitle_path`** on a path Ray can read. Bare SRT uploads alone return `subtitle_required` / `file_path is required`.
- **`source_language`:** omit on `subtitle_source=translate` — Ray returns `source_language_not_used` (only `automatic` / `generate_from_audio` may send it).
- The plugin writes one temp SRT per media file to **`/volume33/ray-output/lingarr-tmp/`** and submits paths as **`/out/lingarr-tmp/…`** (Ray’s usual `ray-output` → `/out` mount). Override those constants if your layout differs.
- Lingarr translate does **not** need the video file — only the EN SRT batch (unlike Ray create-from-video).
- **Quota pause ≠ Lingarr Failed:** newer Ray keeps Cloud jobs **Queued** with `allowance_wait.code=cloud_daily_video_limit_reached` until midnight UTC (or room frees). The plugin still polls; if `ray_job_timeout_minutes` elapses first, Lingarr marks the request Failed (`TaskCanceledException` / “All configured batch translation services failed”) while Ray still shows Queued. Raise the timeout (default 1440); do not mass-Retry the same UTC day.
- **Quota:** a Ray Cloud fallback can consume Ray Cloud’s **daily whole-video allowance** (same counter as create-from-video; resets midnight UTC). Cloud jobs may remain queued until allowance resets; keep `ray_job_timeout_minutes` at 1440. Drip-feed Lingarr automation.
- **Do not confuse** Ray Jobs **Failed** `track_choice_required` (“more than one audio track”) on create-from-video / multi-audio files with quota pause or Lingarr SRT translate jobs (`lingarr-ray-*.srt`).

## Known Ray issues (upstream, as of 2026-11-19)

Confirmed by Ray support (Ral) against Headless **4.0.18-beta.1**:

1. **`local_stalled` / “no progress for 30 minutes” at translating ~40%** — Local translate progress is not reported to the 30-minute safety timer. Long CPU Sunlight jobs are killed while still working. Fix forthcoming; until then prefer shorter files, `ray_quality=fast`, DeepL, or Cloud, and avoid auto-retry loops on the same stall.
2. **`local_subtitle_language_unknown`** — Local may still fail language detection on temp `lingarr-ray-*.srt` names. Do **not** send `source_language` on translate (Ray rejects it); language tag in the filename is the remaining lever if this returns.
3. **Arc ASR GPU (`RAY_ASR_GPU=on`)** helps speech / nearby-check only; **translation remains on CPU**.
4. Support via **`POST /v1/support/messages`** (+ remote-help on a failed job) is more reliable than Outlook → Intercom when mail NDRs.

## License

MIT (this plugin). Ray itself is a third-party product; follow Ray’s terms and licensing.
