# Retrieval and refresh

Current-state guide — see [review baseline](../architecture.md).

Infrastructure adapters retrieve official structured API/HTTP/HTML data; no production Playwright package. Application orchestrates freshness, bounded refresh and independent persistence. Exact-date verification, provenance, source states and last-known-good remain mandatory. Phase 34B-2 added official POST coalescing accounting for Accesso request nonce (not identical raw bodies); Phase 34C consolidated shared Accesso transport/evidence without changing source-specific mapping. A dormant browser-session seam remains intentionally. Date switches do not retrieve online.

## Weather (Phase 37B — implemented, not activated)

Weather is a separate path, not an official Carowinds source and not part of `OfficialDataRefreshCoordinator`, the refresh orchestrator, `SourceResult` states or the scheduled task. `Domain/Weather` holds the models, the two verified park reference points (`CarowindsWeatherLocation.Park`, decision D8) and `WeatherAlertRules`. `Application/Weather` holds the `IWeatherProvider` and `IWeatherCacheStore` ports, `WeatherRefreshPolicy`, the single-flight `WeatherRefreshCoordinator` (refuses every request for an unverified location) and `WeatherSnapshotBuilder`; `Application/Links/WeatherSourceLinkPolicy` allows only the four NWS hosts. `Infrastructure/Weather/Nws` holds its own `NwsHttpTransport` (own `HttpClient`, `api.weather.gov` only, versioned User-Agent with the public repository URL, conditional requests, 2 MB body cap), the parsers and `NwsWeatherProvider`. Request policy, alert handling and failure states: [Phase 37B](../phase37b-weather-retrieval-and-cache.md). Not registered in the Desktop composition root; Phase 37C activates it.

## Evidence and detailed implementation records

- [phase31-dynamic-seasons-and-deals.md](../phase31-dynamic-seasons-and-deals.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
- [phase34c-repository-cleanup.md](../phase34c-repository-cleanup.md)
