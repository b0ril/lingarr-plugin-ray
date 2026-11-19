# Lessons learned — Lingarr.Plugin.Ray

Ops-facing history for this plugin and Ray Headless. Stack Compose / NAS pins live in [`b0ril/multimedia`](https://github.com/b0ril/multimedia) (`LESSONS-LEARNED.md`, `newnas/ray-lingarr-plugin/`).

## 2026-11-19 — Ray `local_stalled` at translating / 40% (upstream)

**Symptom:** Lingarr → Ray Local Sunlight (`quality=best`) jobs fail after exactly ~1800s with:

`error_code=local_stalled` — “Ray stopped this local job - it made no progress for 30 minutes”

Progress stuck at **40%**, stage **`translating`**. Short files often succeed (~15–20s); long files die mid-work.

**Root cause (Ral / Ray support, 2026-11-19):** Ray bug, **not** Lingarr or NAS misconfig. The Developer API safety timer stops a local job after 30 minutes with no reported progress. Translation advances section-by-section but **never feeds that progress to the timer**. On CPU (translation stays on CPU even with Arc ASR GPU), long SRTs take >30 minutes; Ray kills a still-running job (logs showed ~60% done on one sample). No customer setting disables the timer today.

**Also confirmed by support:**

| Issue | Reality |
|-------|---------|
| Cloud `waiting_for_allowance` | Not stuck. Daily admit was **5 videos/day** at 00:00 UTC; large queues take months. Ray checking plan allowance. |
| `local_subtitle_language_unknown` | Temp `lingarr-ray-*.srt` names have no language tag. Ray **ignores** `source_language` / `subtitle_source_language` on **local** jobs today (upstream bug). Fix coming; plugin already sends `source_language` (v1.2.6). |

**Plugin / ops response (v1.2.6):**

- Send **`source_language`** on translate submits when Lingarr knows it (ready for Ray’s fix).
- Setting **`ray_quality`**: `best`/`sunlight` (default) or `fast`/`moonlight` — Moonlight is lighter on Local CPU and less likely to exceed 30 minutes until Ray ships the progress-timer fix.
- Until Ray updates: avoid auto-retry storms on long Local failures; prefer DeepL / Cloud for heavy files; do not mass-Retry the same stalls.

**Support path:** Ray `/v1/support/messages` (`sent: true`) + remote-help code for a failed job. Outlook → Intercom sometimes NDRs (`550 5.7.501` spam/IP); API support is the reliable channel.

**Do not:** treat these stalls as “Lingarr deleted the temp SRT mid-job” (cleanup is in `finally` after wait) or as concurrency overload (peak concurrent Lingarr→Ray translate observed = 1).

## Earlier (pointers)

- DeepL 413 / blank cues / Ray poll `ResponseEnded` retries → plugin ≥1.2.2.
- Cloud daily admit vs Lingarr Failed while Ray still Queued → `ray_job_timeout_minutes=1440`.
- Daytime Cloud conservation → `ray_cloud_fallback=false` (plugin ≥1.2.5) + multimedia 22:00 Sofia spillover.
