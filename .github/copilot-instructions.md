# Copilot instructions — lingarr-plugin-ray

Canonical **Lingarr.Plugin.Ray** source (v1.2+). Stack ops / Compose live in [`b0ril/multimedia`](https://github.com/b0ril/multimedia); do **not** put NAS host paths or live secrets here.

Lessons / upstream Ray bugs: [`LESSONS-LEARNED.md`](../LESSONS-LEARNED.md).

## Contract

| Item | Value |
|------|--------|
| Provider id | `ray` |
| Assembly | `Lingarr.Plugin.Ray` |
| Version | **1.2.7** |
| Plugin API | 1.0 (`LingarrPluginApiVersion`) |
| Interfaces | `ITranslationService`, `IBatchTranslationService` |
| Request helper | `RayTranslateRequest` (quality normalize + translate body) |
| Setting keys | Required: `ray_endpoint`, `ray_api_key`, `ray_target`, `ray_quality`, `ray_job_timeout_minutes`. Optional: `ray_libretranslate_endpoint`, `ray_deepl_api_key`, `ray_provider_order`, `ray_cloud_fallback`. DeepL key normally from `deepl_api_key`. |
| Tests | `dotnet test Lingarr.Plugin.Ray.Tests … -p:LingarrContractsPath=/path/to/Lingarr.Contracts.dll` |

## Hard behaviour (do not regress)

- **One Lingarr subtitle file → one Ray job** (`max_batch_size=0` on the host). No cue chunking (multiplies Cloud admits).
- **Provider order:** default = Lingarr translation-services UI order (1.5.0+); non-empty `ray_provider_order` overrides (`ray`, `libretranslate`, `deepl`). Batch mode never calls built-in DeepL; this plugin runs the chain. DeepL key = **`deepl_api_key`** on the DeepL service instance — do not ask users to paste it under Ray Local.
- **DeepL:** JSON, split **&lt;96 KiB**, blank cues omitted. HTTP **456** / usage → sticky-skip until `/v2/usage` recovers.
- **LibreTranslate:** `/translate`, split **&lt;64 KiB**, blanks preserved locally. Free fallback, not DeepL-equivalent.
- **Ray HTTP:** JSON POSTs use **`ByteArrayContent` + Content-Length** (Ray **411** on chunked). Poll `/v1/jobs` and result download: retry transient drops (`ResponseEnded`); do not fail the subtitle on one cut.
- **Translate submit:** `subtitle_source=translate`, **`file_path` + `subtitle_path`** on a Ray-readable path; **omit `source_language`** (Ray `source_language_not_used` on translate); **`quality`** from `ray_quality` (`best`/`sunlight` or `fast`/`moonlight`).
- Temp layout: Lingarr `/volume33/ray-output/lingarr-tmp/` ↔ Ray `/out/lingarr-tmp/`.
- **`ray_target=cloud`** shares Cloud’s **daily whole-video admit** (resets **midnight UTC**).
- `ray_job_timeout_minutes` default **1440**, clamp **1–2880**.
- Do not treat Ray UI **`track_choice_required`** as a plugin/translate bug.

## Known Ray upstream (do not “fix” in the plugin)

As of **2026-11-19** (Ray support confirmation on 4.0.18-beta.1):

- **`local_stalled` @ translating/40%:** Local progress not reported to the 30‑minute safety timer; long CPU translates are killed while still working. No setting disables it. Mitigation until Ray ships a fix: `ray_quality=fast`, DeepL/Cloud, stop auto-retry storms — **not** patching Ray.
- **`local_subtitle_language_unknown`:** Local may fail detection on untagged temp SRTs; do not send `source_language` on translate (Ray rejects it).
- Translation stays on **CPU** even with Arc ASR GPU.
- Prefer Ray **`/v1/support/messages`** (+ remote-help) over Outlook→Intercom when mail NDRs.

## Build / install

See `README.md`. Dockerfile base default: **`lingarr/lingarr:1.5.0`**. Maintainer stack: multimedia `install-lingarr-ray-plugin.sh` + `DOCKER_CONTEXT=newnas` (pin this repo’s `main` SHA after each release). On Lingarr ≥1.5.0, plugin settings are under `/api/translationservice/{rayId}/settings`.

## Change discipline

| Change | Where |
|--------|--------|
| Plugin C# / Dockerfile / unit tests / this file / `LESSONS-LEARNED.md` | **This repo only** |
| NAS Compose, live settings, spillover cron, install script SHA pin | **multimedia** / NAS |
| Never | API keys, Cursor / agent co-author trailers |
