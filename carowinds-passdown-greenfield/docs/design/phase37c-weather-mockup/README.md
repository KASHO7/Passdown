# Approved Dashboard Weather mock-up (Phase 37C, decision D4): tracked reference set

The owner approved the Dashboard Weather card mock-up on 2026-11-27 ("Approve mock-up"; recorded in
[Phase 37A, later sign-off](../../phase37a-weather-source-architecture-plan.md#later-sign-off-d4-mock-up-approved-2026-11-27)).
The full reference (interactive HTML, 13 renders, design notes) is in the git-ignored
`artifacts/design-review/Phase37C-WEATHER-MOCKUP/`. These four renders are unchanged copies (same bytes, same
SHA-256), kept so the approved design survives without that local folder.

**Sample data.** Every weather value in these images was invented for review. They are not NWS results and must
never be used as application data or test expectations. They show the browser mock-up, not the Avalonia
application: the implemented card is evidenced by `tools/visual-fixtures` captures (see the
[Phase 37C record](../../phase37c-dashboard-weather-integration.md)).

| File | State | SHA-256 |
|---|---|---|
| `01-light-normal.png` | Light, normal conditions | `db77026471cf0567cec2154a9c40c935e19e3f3368b00614da05aaefb007a45f` |
| `02-dark-normal.png` | Dark, normal conditions | `7729a47594ef3fc1bf070b256e868cccf629deb1821469f6ce0ed19a23e26dab` |
| `05-light-alert-expanded.png` | Two active alerts, details expanded (Refresh Weather, safety note) | `c5e260155d1d2feb8b3564f88c0be4bf4dc2484fccf1ea8d6c162c163c4eed81` |
| `07-light-stale.png` | Stale: refresh failing, alert status unknown | `0159eb030de2fd5228dfc94dfff616f53740aa39e0e6e006a9ac85e24bc471da` |
