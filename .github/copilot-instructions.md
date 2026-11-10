# Copilot instructions — lingarr-plugin-ray

Canonical **Lingarr.Plugin.Ray** source (v1.2+). Stack ops live in [`b0ril/multimedia`](https://github.com/b0ril/multimedia) (`.github/copilot-instructions.md`); do not put NAS Compose paths here.

## Contract

| Item | Value |
|------|--------|
| Provider id | `ray` |
| Assembly | `Lingarr.Plugin.Ray` |
| Version | **1.2.0** (configurable DeepL ↔ Ray order) |
| Plugin API | 1.0 (`LingarrPluginApiVersion`) |
| Interfaces | `ITranslationService`, `IBatchTranslationService` |
| Setting keys | `ray_endpoint`, `ray_api_key`, **`ray_deepl_api_key`**, **`ray_provider_order`**, `ray_target`, `ray_job_timeout_minutes` |
| Tests | `Lingarr.Plugin.Ray.Tests` — `dotnet test … -p:LingarrContractsPath=/path/to/Lingarr.Contracts.dll` |

## Hard behaviour (do not regress)

- **One Lingarr subtitle file → one Ray job** when Ray runs (`max_batch_size=0` on the host). Do not reintroduce cue chunking; it multiplies Cloud admits.
- **`ray_provider_order`:** comma-separated `deepl` / `ray` (any order or single). Default `deepl,ray`. Empty + DeepL key → `deepl,ray`; else `ray`. Unknown tokens ignored; DeepL entries dropped without `ray_deepl_api_key`.
- **DeepL:** one multi-text `/v2/translate` per file; Pro → `api.deepl.com`, Free `:fx` → `api-free.deepl.com`. Quota (`AnyLimitReached` / HTTP **456**) sticky-skips DeepL until usage recovers. Non-quota DeepL errors → next provider for that file only.
- **Lingarr SERVICE_TYPE:** keep **`["ray"]` only**. Do not chain built-in DeepL — batch mode skips it; order belongs in this plugin.
- JSON POSTs to Ray use **`ByteArrayContent` + Content-Length**. Ray rejects chunked bodies with HTTP **411**.
- Translate submit: `subtitle_source=translate` with **`file_path` and `subtitle_path`** on a path Ray can read.
- Default temp layout: Lingarr `/volume33/ray-output/lingarr-tmp/` ↔ Ray `/out/lingarr-tmp/`.
- **`ray_target=cloud`** shares Ray Cloud’s **daily whole-video admit** counter (resets **midnight UTC**).
- `ray_job_timeout_minutes` default **1440**, clamp **1–2880** (outlast `allowance_wait`).
- Do not treat Ray UI **Failed** `track_choice_required` as a plugin/translate bug.

## Build / install

See `README.md`. Dockerfile default base is **`lingarr/lingarr:1.4.1`**. Host installs: `DOCKER_CONTEXT=newnas` + multimedia `install-lingarr-ray-plugin.sh` from **`main`**.

## Change discipline

- Plugin C# / Dockerfile / unit tests → **this repo only**.
- NAS Compose / live DeepL key / provider order UI values → **multimedia** / NAS.
- Never commit API keys or Cursor / agent co-author trailers.
