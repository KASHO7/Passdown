# RC.15 GitHub release notes

Body of the GitHub prerelease **"Carowinds Operations v0.1.0-rc.15"** (tag `v0.1.0-rc.15`, prerelease, not latest).
Same format as the published RC.14 body. Publication authorized by the product owner on 2026-11-28 (owner brief
"Carowinds Operations — Publish RC.15"); the hash and test-count lines were re-checked against the final release gates
(`docs/releases/rc15-release-preparation.md` §21). The posted body is everything below the line.

---

# Carowinds Operations RC.15

RC.15 adds National Weather Service weather to the Dashboard, the Calendar and Passdown PDFs, and keeps verified daily
ticket prices after the park closes. Official Carowinds sources, pricing rules, the database and the Personal Sync
format are unchanged.

## Weather Information

- Weather from the U.S. National Weather Service (NWS) for Carowinds' fixed location, covering both the North Carolina
  and South Carolina sides of the park. The app never uses the computer's location or GPS.
- **Dashboard Weather card:** conditions from a nearby NWS station (named, with its time), today's forecast high and
  low, the highest rain chance during park hours, and official NWS alerts in effect. "No active NWS alerts" is shown
  only when the alert check is current; otherwise the card says when alerts were last checked. Refresh Weather is in
  the expanded weather details.
- **Calendar:** the selected-date panel shows a compact forecast for that date's park hours (or a general forecast on a
  closed day), with an hourly breakdown you can expand. Past dates and dates beyond the forecast show no weather
  values. The month grid never shows weather.
- Weather is checked only while the app is open, following NWS caching guidance with minimum intervals and back-off.
  Selecting dates or switching screens never goes online. Offline, the app shows the last weather it received and
  marks it when it may be out of date; it never invents values.
- Weather is informational. It does not replace official warnings, lightning procedures or other park safety
  decisions.

## Passdown and PDF Improvements

- **Weather Outlook in Passdown PDFs** (Classic, Compact and Modern): a small forecast for the date's park hours —
  condition, temperature range, "up to" rain chance and a small NWS attribution. It is left out, without leaving a
  gap, when no current forecast covers the remaining park hours. PDFs never show weather alerts.
- Choose it with **Weather Outlook** under Overview › Include in Passdown, or **Include Weather Outlook by default** in
  Settings › Passdown Defaults. Both are the same setting, saved on this computer only (not synced). It is on unless
  you turn it off.
- **Weather in printed Passdowns.** Print, Save PDF and Create PDF always use the latest weather the app has. If new
  weather arrives while the preview is open, the printed or saved PDF can show newer weather than the preview did; the
  preview updates to match right after.
- Finalized History is unchanged: it opens the original stored PDF and never adds weather.

## Pricing Accuracy

- After the park closes, Carowinds' official sales calendar stops listing that day. Daily Admission, Fast Lane and other
  dated ticket prices that were already verified for the day are now kept instead of disappearing, together with their
  original verification time.
- These prices are labelled as previously verified on Products & Pricing, the Dashboard and the PDF, and the Calendar's
  Daily ticket line reads, for example, "$49.00 · last verified Nov 27". A previously verified price is the last price
  Carowinds published for that date, **not** confirmation that tickets are still on sale.
- A later official check still replaces the price when Carowinds publishes a new one. Display-price overrides keep
  working as before.

## Reliability and Performance

- Maintenance with no change to how screens work: a simpler internal Early Ride Time editor, more reliable UI-thread
  handling, and stronger automated test and repository safeguards.
- Weather work starts a few seconds after the window opens and runs in the background, so it does not delay startup.

## Compatibility

- No database change from RC.11, RC.12, RC.13 or RC.14 (schema 18). Personal Sync format unchanged — still update every
  computer that shares a sync folder.
- Existing drafts, Notes, Price History, display-price overrides, Personal Sync settings, finalized History PDFs, PDF
  styles and appearance settings remain compatible.
- Weather data and the Weather Outlook setting stay on each computer; they are not shared through Personal Sync and are
  not stored in drafts, the database or finalized History.

## Known limitations

- Weather depends on the public NWS service. NWS publishes no guaranteed request allowance; if it is slow or limiting
  requests, the app waits and retries and shows the last weather it has, marked as such.
- Weather that is too old is not used in a PDF (more than three hours), and the Calendar marks an old forecast as
  possibly out of date.
- Printed output is still a high-resolution image of each page (unchanged since RC.12).
- Display-price overrides are numbers only.
- An installation of a LOCALTEST pre-release test build will not be offered RC.15 in-app; extract the ZIP instead.

## Install / update

- **From RC.14 (or RC.13 / RC.12 / RC.11):** Settings → Application Updates → Check for Updates → Install Update →
  Restart and Install. The updater checks the signature and every file, and backs up the database first.
- **By hand:** download `Carowinds-Operations-0.1.0-rc.15-win-x64.zip`, extract to a new folder and run
  `Carowinds.Passdown.Desktop.exe`. Windows 11 x64; nothing else to install.
- SHA-256 (ZIP): `64b5d17b7921a48dbdf8c3eec9ebbbcb61a3cb509bfe9720079cbed4973981dc`
- The `.update.json` / `.update.json.sig` files are the signed update manifest used by the in-app updater.

## Verification

- Automated: 6,569 deterministic tests passed, 0 skipped; clean Release build, 0 warnings.
- Windows: the integrated Phase 37E2 local test build was accepted by the product owner on Windows. The RC.14 → RC.15
  in-app update has not been run yet; it needs the published release assets and is checked right after publication.

Source commit: `19cbf8b`. Prerelease, Windows x64 portable.
