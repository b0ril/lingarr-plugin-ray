# Copilot instructions — lingarr-plugin-ray

Canonical **Lingarr.Plugin.Ray** source. Stack ops live in [`b0ril/multimedia`](https://github.com/b0ril/multimedia) (`.github/copilot-instructions.md`); do not put NAS Compose paths here.

## Contract

| Item | Value |
|------|--------|
| Provider id | `ray` |
| Assembly | `Lingarr.Plugin.Ray` |
| Plugin API | 1.0 (`LingarrPluginApiVersion`) |
| Interfaces | `ITranslationService`, `IBatchTranslationService` |
| Setting keys | `ray_endpoint`, `ray_api_key`, `ray_target`, `ray_job_timeout_minutes` |

## Hard behaviour (do not regress)

- **One Lingarr subtitle file → one Ray job** (`max_batch_size=0` on the host). Do not reintroduce cue chunking; it multiplies Cloud admits.
- JSON POSTs use **`ByteArrayContent` + Content-Length**. Ray rejects chunked bodies with HTTP **411**.
- Translate submit: `subtitle_source=translate` with **`file_path` and `subtitle_path`** on a path Ray can read. Bare upload-only flows fail (`subtitle_required` / `file_path is required`).
- Default temp layout: Lingarr `/volume33/ray-output/lingarr-tmp/` ↔ Ray `/out/lingarr-tmp/` (override constants if the host differs).
- **`ray_target=cloud`** shares Ray Cloud’s **daily whole-video admit** counter with create-from-video (resets **midnight UTC**).
- Newer Ray **pauses** Cloud work as Jobs **Queued** + `allowance_wait` (`cloud_daily_video_limit_reached`). That is not a Ray failure. `WaitForCompletionAsync` must keep polling until complete/failed/canceled **or** `ray_job_timeout_minutes` (default **1440**, clamp **1–2880**). A short timeout marks Lingarr Failed while Ray is still Queued.
- Do not treat Ray UI **Failed** `track_choice_required` (multi-audio create-from-video) as a plugin/translate bug.

## Build / install

See `README.md`. Hosts that volume-mount the DLL (e.g. multimedia `install-lingarr-ray-plugin.sh`) rebuild against the live `lingarr/lingarr:<tag>` Contracts DLL.

## Change discipline

- Plugin C# / this Dockerfile → **this repo only**.
- NAS Compose, cron, Ray helpers → **multimedia**.
- Never commit API keys or Cursor / agent co-author trailers.
