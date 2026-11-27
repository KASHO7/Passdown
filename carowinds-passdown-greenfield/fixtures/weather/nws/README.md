# NWS weather fixtures (Phase 37B)

Deterministic inputs for the weather parser, provider and coordinator tests. No test reads the network.

| File | Origin |
|---|---|
| `points-nc-check-35.1037-80.9426-2026-11-27.json` | Live `GET /points/35.1037,-80.9426`, 2026-11-27 (unchanged) |
| `points-sc-check-35.09-80.94-2026-11-27.json` | Live `GET /points/35.09,-80.94`, 2026-11-27 (unchanged) |
| `forecast-gsp-116-59-2026-11-27.json` | Live 12-hour forecast, 2026-11-27 (unchanged, 14 periods) |
| `forecast-hourly-gsp-116-59-2026-11-27-first30.json` | Live hourly forecast, 2026-11-27, trimmed to the first 30 of 156 periods |
| `stations-gsp-116-59-2026-11-27-first3.json` | Live stations list, 2026-11-27, trimmed to the nearest three |
| `observation-latest-kclt-2026-11-27.json`, `observation-latest-kuza-2026-11-27.json` | Live latest observations, 2026-11-27 (unchanged) |
| `alerts-active-none-2026-11-27.json` | Live `/alerts/active?point=35.1028,-80.9417`, 2026-11-27 (no alerts) |
| `alerts-active-synthetic-multiple.json` | **Synthetic**: invented alerts shaped like the API (update, cancel, test, expired, missing id, missing end). Not a real NWS product |

The live responses are public NWS open data and contain no personal information. Refresh them only with a
deliberate, bounded live check, and keep the dates in the file names.
