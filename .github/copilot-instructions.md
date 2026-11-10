# Copilot instructions — lingarr-plugin-ray

Canonical **Lingarr.Plugin.Ray** source (v1.1+). Stack ops live in [`b0ril/multimedia`](https://github.com/b0ril/multimedia) (`.github/copilot-instructions.md`); do not put NAS Compose paths here.

## Contract

| Item | Value |
|------|--------|
| Provider id | `ray` |
| Assembly | `Lingarr.Plugin.Ray` |
| Version | **1.1.0** (DeepL primary + Ray fallback) |
| Plugin API | 1.0 (`LingarrPluginApiVersion`) |
| Interfaces | `ITranslationService`, `IBatchTranslationService` |
| Setting keys | `ray_endpoint`, `ray_api_key`, **`ray_deepl_api_key`** (optional), `ray_target`, `ray_job_timeout_minutes` |
| Tests | `Lingarr.Plugin.Ray.Tests` — `dotnet test … -p:LingarrContractsPath=/path/to/Lingarr.Contracts.dll` |

## Hard behaviour (do not regress)

- **One Lingarr subtitle file → one Ray job** when Ray runs (`max_batch_size=0` on the host). Do not reintroduce cue chunking; it multiplies Cloud admits.
- **Optional DeepL (`ray_deepl_api_key`):** when set, each batch checks `/v2/usage` then translates all cues in **one** DeepL request (Pro → `https://api.deepl.com`, Free `:fx` → `https://api-free.deepl.com`). On quota (`AnyLimitReached` or HTTP **456**), fall back to **one** Ray job for the **whole file**, then **sticky-skip** DeepL until a later usage check shows room (process-lifetime gate; restart clears). Non-quota DeepL errors → Ray for that file only (no sticky). Keep Lingarr `SERVICE_TYPE` on **`ray`** — do not also enable built-in DeepL unless a separate chain is intentional.
- JSON POSTs to Ray use **`ByteArrayContent` + Content-Length**. Ray rejects chunked bodies with HTTP **411**.
- Translate submit: `subtitle_source=translate` with **`file_path` and `subtitle_path`** on a path Ray can read. Bare upload-only flows fail (`subtitle_required` / `file_path is required`).
- Default temp layout: Lingarr `/volume33/ray-output/lingarr-tmp/` ↔ Ray `/out/lingarr-tmp/` (override constants if the host differs).
- **`ray_target=cloud`** shares Ray Cloud’s **daily whole-video admit** counter with create-from-video (resets **midnight UTC**).
- Newer Ray **pauses** Cloud work as Jobs **Queued** + `allowance_wait` (`cloud_daily_video_limit_reached`). That is not a Ray failure. `WaitForCompletionAsync` must keep polling until complete/failed/canceled **or** `ray_job_timeout_minutes` (default **1440**, clamp **1–2880**). A short timeout marks Lingarr Failed while Ray is still Queued.
- Do not treat Ray UI **Failed** `track_choice_required` (multi-audio create-from-video) as a plugin/translate bug.

## Build / install

See `README.md`. Dockerfile default base is **`lingarr/lingarr:1.4.1`**. Hosts that volume-mount the DLL (e.g. multimedia `install-lingarr-ray-plugin.sh`) rebuild against the live `lingarr/lingarr:<tag>` Contracts DLL from **`main`**.

## Change discipline

- Plugin C# / this Dockerfile / unit tests → **this repo only**.
- NAS Compose, cron, Ray helpers, DeepL key in the live UI → **multimedia** / NAS.
- Never commit API keys or Cursor / agent co-author trailers.
