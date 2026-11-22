# Lessons learned — Lingarr.Plugin.Ray

Ops-facing history for this plugin and Ray Headless. Stack Compose / NAS pins live in [`b0ril/multimedia`](https://github.com/b0ril/multimedia) (`LESSONS-LEARNED.md`, `newnas/ray-lingarr-plugin/`).

## 2026-11-23 — Lingarr 1.5.0 + plugin 1.2.7 (`source_language_not_used`)

**Symptom:** Daytime Lingarr Failed with `All configured providers failed (DeepL → Ray Local)` and Ray HTTP 400 `source_language_not_used` (“source_language is only for subtitles made from the audio”).

**Cause:** Plugin 1.2.6 sent `source_language` on `subtitle_source=translate`. Ray now rejects that field unless the job is `automatic` / `generate_from_audio`. Separately, DeepL Free often sits at `500000/500000`, so the chain falls through to Ray Local and long files hit `local_stalled`.

**Fix:** Plugin **1.2.7** omits `source_language` on translate submits. Build against Hub **`lingarr/lingarr:1.5.0`**. On Lingarr 1.5.0, plugin settings move to `/api/translationservice/{id}/settings`; keep services UI **DeepL → Ray** and `ray_provider_order=deepl,ray`.

**Do not:** re-add `source_language` “for Local language detect” — that is the regression.

## 2026-11-19 — Ray `local_stalled` at translating / 40% (upstream)

**Symptom:** Lingarr → Ray Local Sunlight (`quality=best`) jobs fail after exactly ~1800s with:

`error_code=local_stalled` — “Ray stopped this local job - it made no progress for 30 minutes”

Progress stuck at **40%**, stage **`translating`**. Short files often succeed (~15–20s); long files die mid-work.

**Root cause (Ral / Ray support, 2026-11-19):** Ray bug, **not** Lingarr or NAS misconfig. The Developer API safety timer stops a local job after 30 minutes with no reported progress. Translation advances section-by-section but **never feeds that progress to the timer**. On CPU (translation stays on CPU even with Arc ASR GPU), long SRTs take >30 minutes; Ray kills a still-running job (logs showed ~60% done on one sample). No customer setting disables the timer today.

**Also confirmed by support:**

| Issue | Reality |
|-------|---------|
| Cloud `waiting_for_allowance` | Not stuck. Daily admit was **5 videos/day** at 00:00 UTC; large queues take months. Ray checking plan allowance. |
| `local_subtitle_language_unknown` | Temp `lingarr-ray-*.srt` names have no language tag. |
| `source_language_not_used` | Ray **rejects** `source_language` on `subtitle_source=translate` (only `automatic` / `generate_from_audio`). |

**Plugin / ops response (v1.2.7):**

- **Omit `source_language`** on translate submits (v1.2.6 sending it caused HTTP 400 / Lingarr Failed).
- Setting **`ray_quality`**: `best`/`sunlight` (default) or `fast`/`moonlight` — Moonlight is lighter on Local CPU and less likely to exceed 30 minutes until Ray ships the progress-timer fix.
- Until Ray updates: avoid auto-retry storms on long Local failures; prefer DeepL / Cloud for heavy files; do not mass-Retry the same stalls.

**Support path:** Ray `/v1/support/messages` (`sent: true`) + remote-help code for a failed job. Outlook → Intercom sometimes NDRs (`550 5.7.501` spam/IP); API support is the reliable channel.

**Do not:** treat these stalls as “Lingarr deleted the temp SRT mid-job” (cleanup is in `finally` after wait) or as concurrency overload (peak concurrent Lingarr→Ray translate observed = 1).

## Earlier (pointers)

- DeepL 413 / blank cues / Ray poll `ResponseEnded` retries → plugin ≥1.2.2.
- Cloud daily admit vs Lingarr Failed while Ray still Queued → `ray_job_timeout_minutes=1440`.
- Daytime Cloud conservation → `ray_cloud_fallback=false` (plugin ≥1.2.5) + multimedia 22:00 Sofia spillover.
