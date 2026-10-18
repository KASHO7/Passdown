# Carowinds Operations 0.1.0-rc.8 — release notes

Release candidate for controlled testing on Windows 11 x64. Built from commit `a651e26`.

| Milestone | Status |
|---|---|
| Built | **Yes**: Release build 0 warnings / 0 errors; all automated tests pass (see `docs/release-manifest.md` "0.1.0-rc.8") |
| Packaged | **Yes**: portable ZIP (recommended) + experimental single-file EXE |
| Update manifest | **Signed locally** with `carowinds-release-1`; **not uploaded** |
| Windows tested | **No**: `docs/releases/rc8-windows-validation.md` is entirely NOT EXECUTED |
| Distribution approved | **No**: QuestPDF licence approval, Fury photograph rights and organisation approval for GitHub distribution are all pending |

Download names, sizes and SHA-256 checksums: `docs/release-manifest.md` "0.1.0-rc.8".
How to install: `docs/releases/rc8-installation-guide.md`.

## What's new since RC.7

Your drafts, notes, prices, History, Personal Sync settings and other data are kept as they are. This
version does not change how anything is stored.

### Reliability

- **No more freeze after a failed save.** In RC.7, if a draft could not be saved (for example because the
  data folder was read-only), changing the date could make the app retry forever and use a whole CPU core.
  Now the app tries once and stops.
- **Retry Save.** When a save fails, a **Retry Save** button appears on the Passdown screen. Nothing retries
  in the background on its own.
- **Clearer failed-save message.** The Draft status says **"Save failed — changes not saved"** and the
  reason is shown when you hover over it.
- **Your unsaved edits are protected.** While a save is failing, changing the date, **Check Online** and
  operational-note changes are held back with an explanation, so the edits on screen are not thrown away.

### Dashboard

- **Shows no longer overlap.** Long lists of show times no longer run into the venue. Each show lists its
  name, then its times, then its venue.
- **Compact lists with "+N more".** A show with many times lists the first few and a **+N more** button that
  shows all of them; click again to collapse.
- **Simpler banner.** The Fury 325 banner shows just "CAROWINDS OPERATIONS", without the small chart icon.
  The app icon in the taskbar is unchanged.

### Appearance

- Settings → **Appearance** now offers **Use Windows setting** (the default, same as before), **Light** and
  **Dark**.
- The change applies immediately, without restarting.
- Your choice is remembered on this computer only. It is not shared with other computers through
  Personal Sync.

### Updates

- **Check for updates from inside the app.** Settings → **Application Updates** can look for newer
  approved versions. A small dot on the Settings gear means one is available; there are no pop-ups.
- **You decide.** Nothing is downloaded until you choose **Install Update**, and the app does not close
  until you choose **Restart and Install**.
- **Signed updates only.** The app checks each update's digital signature and every file's checksum before
  installing it. If anything doesn't match, the update is refused.
- **Safe fallback.** If the new version fails to start, the previous version is put back and reopened.
- **What's New.** After a successful in-app update, the new version's notes open once. You can reopen
  them any time from Settings → **About this build** → **What's New**, even offline.

**RC.8 itself has to be installed by hand.** RC.7 and earlier cannot update themselves, so this version
is installed by extracting the ZIP, as before. In-app updates are possible from RC.8 onward, once a
future version has been approved and published and the updater has passed Windows testing.

## Known limitations

- **Not yet tested on Windows.** Everything above was built and tested automatically on a Mac. None of
  it, including the updater, has been run on a Windows PC yet.
- **Saving can pause briefly.** If the data file is busy, one save attempt can take up to about five
  seconds before it reports a result.
- **Closing twice after a failed save loses unsaved changes.** The first close while a save is failing
  shows a warning and keeps the app open. Closing again closes the app and discards the unsaved edits.
- **The experimental single .exe cannot update itself.** Its Application Updates section says *Manual
  update required*. It also lacks the licence notices and automatic-refresh helpers, so use the ZIP.
- **First manual install shows no What's New.** That is intentional. Open it from Settings → About this
  build.
- **Computers sharing a Personal Sync folder** should all be upgraded together, as with earlier versions.
- **Not approved for distribution.** QuestPDF licence approval, rights to the Fury 325 photograph and
  organisation approval for publishing on GitHub are still pending. This build is for controlled testing
  only.
