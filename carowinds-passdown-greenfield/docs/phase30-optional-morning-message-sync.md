# Phase 30 — Optional Morning Message Sync

> **RETIRED (2026-10-23).** The Morning Message Builder, its formatted copy and Morning Message sync were removed by a targeted rollback; see `docs/morning-message-rollback.md`. This document is kept unchanged as a historical record. Nothing it describes exists in the current application.

## Baseline

| Item | Value |
| --- | --- |
| Branch | `feature/passdown-redesign` |
| Starting HEAD | `086b90a` (Phase 29A validation kit; Phase 29 itself is `fb6cc67`) |
| Version in `Directory.Build.props` | `0.1.0-rc.9` / file version `0.1.0.9` (unchanged — no release candidate packaged) |
| Database / Draft / Snapshot schema | 17 / 4 / 2 (unchanged) |
| Personal Sync `SyncSchemaVersion` | 1 (unchanged) |
| Deterministic baseline | 4,838 passed (verified before any change) |

Frozen RC.8 and RC.9 artifacts were not touched, rebuilt or re-hashed. Nothing was pushed or published.

## Objective

Let the user start a morning message on one computer and continue on another — reminders, break plan,
OD Notes and composer preferences — through the Personal Sync folder they already use. Off by default,
chosen per computer, no accounts, no new cloud service, no automatic Teams posting, every Copy for Teams
option unchanged.

## Settings design

Settings → Personal Sync → **Sync Morning Messages** (checkbox inside the Personal Sync panel, above the
draft/note conflict callout).

- Description: "Synchronize morning-message drafts, reminders, break plans, OD Notes, and layout preferences
  across your computers."
- Always shown: "Turning this off stops future synchronization. Messages already saved on this computer will
  remain available."
- Scope note: hours, attendance, pre-solds, prices and the Daily Feature are never copied; the DAP image stays
  on the computer that chose it; the default copy mode also follows you.
- The checkbox is enabled only when Personal Sync is on **and** a folder is chosen. Otherwise it is disabled
  and the screen says "Turn on Personal Sync and choose a sync folder first."
- Status line (exact wording): `Morning Messages: Not syncing` · `Synced` · `Changes waiting to sync` ·
  `Conflict needs review` · `Shared folder unavailable` · `Not syncing — no sync folder`.
- Morning Message conflicts are listed in the same panel (date or "Morning message preferences"), each with
  **Review** → a Part / This Device / Other Device comparison → **Keep This Device**, **Use Other Device**,
  **Cancel**.

## Default and per-computer behavior

- Stored as `MorningMessageSyncEnabled` in this computer's existing `sync-config.json`
  (`%LOCALAPPDATA%\Carowinds Passdown`). Missing in every pre-Phase 30 file, so **existing installations read
  Off**. Every save of that record goes through `with`, so changing the folder or turning Personal Sync on/off
  never resets it, and toggling it never changes the folder, drafts/notes sync or price-history sync.
- Never synchronized itself. Computer A on / Computer B off: A reads and publishes; B reads nothing, writes
  nothing to the shared folder and changes no local file. B can be turned on later and joins with the normal
  merge/conflict rules.

## What synchronizes

Defined in one closed list, `Application/MorningMessage/Sync/MorningMessageSyncFields`.

| Per operating date (`morning-message-content.json`) | Preferences (`morning-message.json`) |
| --- | --- |
| Reminders | Greeting (stored once, not per day — it is a preference) |
| Tentative break plan | Section order and visibility (`Sections`) |
| DAP / Placement text | Ticket-price sections (`PriceSections`) |
| Attach-image reminder (checkbox) | Show every reminder as a bullet |
| OD Notes | Default copy mode (formatted / plain text) |
| Ticket-price note (user commentary) | |

Not synchronized: the DAP image **path** (a local file on one computer); the sync on/off choice; anything
official — park hours, Avenue/North Gate, Carolina Harbor, attendance, pre-solds (Associate draft), official
prices (catalog), the Daily Feature, and the rendered message. Each computer re-composes the message from its
own current data every time. **Clipboard mode**: the default copy mode lives in `morning-message.json`, so it
is included — choosing formatted copy on one computer makes it the default on the others too (it also governs
the Copy menu's Operations Summary / Today's Pricing / Operations + Pricing / OD Notes on those computers).

Dates are the existing operating-date keys (`yyyy-MM-dd`, invariant culture) — never time-zone converted.

OD Notes synchronize, but remain outside **Copy Morning Message**; they are still copied only by **Copy OD
Notes** / Copy ▾ → OD Notes. Their position in the Teams workflow is unchanged.

## Transport and file format

Existing Personal Sync folder only; a new sidecar folder beside the Phase 26 one:

```
Carowinds Operations Sync/
  manifest.json                      (unchanged)
  drafts/ notes/ history/ monthly-ert/ ert-date-overrides/ official-price-history/   (unchanged)
  morning-message/
    <device-id>.json                 one file per computer, written only by that computer
```

```json
{
  "Format": "carowinds-morning-message",
  "FormatVersion": 1,
  "DeviceId": "<this computer's anonymous Personal Sync device id>",
  "Preferences": { "Greeting": { "Value": "Good Morning, team!", "Version": { "<device-id>": 4 } } },
  "Dates": {
    "2026-10-22": {
      "Reminders": { "Value": "Parking meeting at 9:00.", "Version": { "<device-id-A>": 7, "<device-id-B>": 3 } },
      "OdNotes":   { "Value": "…", "Version": { "<device-id-A>": 8 } }
    }
  }
}
```

- **Not a `SyncEnvelope`**: an independently versioned format (`FormatVersion` 1). `SyncSchemaVersion` stays
  **1**; the manifest and every existing folder are untouched. Deterministic text (sorted), rewritten only
  when it changes.
- Only values whose stored version exactly describes them are published (an unrecorded edit waits for the
  next pass), and only fields that have ever held a value — fresh-install defaults and blank boxes are not
  published.
- Sync-provider conflict copies (`<guid>-LAPTOP.json`, `<guid> (1).json`) are read as more versions of that
  computer; temp files and stray files are ignored. Stale temp files (`<guid>.json.tmp-<32 hex>`, older than the
  existing threshold) are removed by the existing `SyncTempFileCleaner`, now covering `morning-message/`.

### Device identity

The existing anonymous Personal Sync device id (`FileDeviceIdProvider`, a random GUID). No Windows user name,
machine name or account appears in any file (tested).

## Merge and conflict model

Each synchronized **field** carries a version vector (device id → that device's edit counter). This computer
remembers, per field, the hash of the value it had at its last pass and that value's version
(`morning-message-sync-state.json`, local only). On each pass, per field
(`MorningMessageFieldReconciliation.Decide`, pure):

1. If the local value's hash differs from the remembered one, it is a local edit: the version gains this
   computer's next counter. (So edits made **while sync was off**, offline, or before a crash are always
   recognized.)
2. Other computers' versions already covered by the local version are ignored (stale copies, old files).
3. A version that **covers** the local one (it was made after seeing this computer's value) replaces it.
4. Two independent versions with **identical** text merge — no conflict.
5. Two independent versions with **different** text are a **conflict**: the local value is kept, the other
   value and its version are stored (in the local state file, surviving restarts) until the user chooses.

Nothing is decided by clocks, file modification times, or which computer synced last.

**Independent fields merge.** Reminders edited on A and the break plan edited on B both survive — each field
is reconciled separately and each side's version provably descends from the common baseline.

**Same field, different edits** → conflict for that date (or for the preferences). Keep This Device / Use Other
Device applies to every conflicting field of that record and writes a version that supersedes both sides, so the
other computers simply adopt the choice on their next pass (tested both ways). With three computers that all
disagree, conflicts surface one other value at a time; no value is dropped without being shown (tested).

**First-time enablement** (A with 30 days, B with 30 overlapping days): nothing is replaced wholesale. Dates
only on the other computer are imported; identical dates merge; dates with different text become conflicts with
each computer keeping its own text; fields left blank on one side are filled from the other. Tested with 30 + 30
days, 10 identical, 5 conflicting, 15 one-sided.

**Preferences on first sync**: a preference still at its fresh-install value adopts the shared one; two
computers that both customized the same preference differently (e.g. section order) get a "Morning message
preferences" conflict — explicit choice, never silent.

**Absence is never deletion**: a missing file, date or field removes nothing anywhere. Clearing a field
synchronizes as an empty value (an edit), not as a deletion; clearing every field keeps an empty entry for the
date. (`FileMorningMessageContentStore.SaveAsync` now keeps an empty entry when a saved date is cleared, and
still writes nothing for a date that never had text — Phase 29's "remove the date" behavior changed for this.)

**60-date retention**: each computer still keeps its 60 most recent dates. Dates older than the newest 60 of
(this computer's dates ∪ incoming dates) are never imported, and a date that ages out locally is "no opinion",
never a deletion. Repeated passes do not ping-pong dates (tested).

## Composer safety

`MorningMessageEditorSession` (one instance shared by the composer and the sync channel):

- While the composer is open on a date, sync never writes that date or the preferences; it reports them as
  waiting. The composer shows: "Changes to this morning message from another computer are waiting. They will
  be added after you close this window; anything you type here is kept."
- Because held changes were not applied, anything typed meanwhile is still an independent edit and becomes a
  conflict instead of silently winning or losing (tested).
- Opening the composer and applying imported changes are serialized by one lock, so a pass can never write
  underneath a composer that is loading. The composer reloads preferences and text every time it opens; the
  Copy menu's default copy mode follows a synchronized change.
- Resolving a conflict for the date that is open in the composer is refused with "Close the Morning Message
  window first, so nothing you are typing is replaced."
- A composer save or close requests a debounced Personal Sync pass (the same `LocalChange` path draft and note
  saves use), only while the option is on.

## Offline, failures and Sync Now

- Editing never depends on the shared folder. If the folder cannot be listed or written, the pass changes
  nothing locally, records "Shared folder unavailable", and the existing coordinator retries on its bounded
  backoff (15 s → 5 min) and periodic cadence — no endless tight retry.
- An individual unreadable or locked file is skipped and retried next pass; other files still flow.
- **Sync Now** runs the same `PersonalSyncService` pass. With the option on, the summary adds e.g.
  "Morning messages: 2 morning messages updated from another computer; 1 morning message conflict to review."
  With it off, nothing Morning Message-related is read, written or reported, and drafts, notes, History, ERT
  and price history sync exactly as before. Morning Message counts are never added to drafts/notes counts.

## Data safety

- **Write order**: local message file(s) first (one atomic replacement each), then the local sync state, then
  this computer's shared file. A version is never published before it is recorded locally; an interruption at
  any point is re-detected safely on the next pass (the recorded hash no longer matches, so the value is
  treated as an edit and merges with the identical remote text).
- **Atomic writes**: every file goes through the existing `AtomicFileWriter` (temp file in the same folder →
  flush to disk → rename). A reader never sees partial JSON.
- **Unreadable local file**: sync never replaces a local `morning-message-content.json` it cannot parse; the
  pass reports a failure and nothing is published.
- **Lost sync state**: treated as a fresh start; this computer's own earlier shared file seeds its edit counter
  so a new edit can never be mistaken for an old one (tested).
- **Validation** (`MorningMessageSyncFileSerializer.Validate`): format name; `FormatVersion` (newer versions
  are skipped and left untouched); device id must match the file name; file ≤ 16 MB; ≤ 120 dates; ≤ 64
  shared files per pass; date keys `yyyy-MM-dd` within 2000–2100; known field names only (unknown ones are
  ignored for forward compatibility); text ≤ 20,000 characters (greeting ≤ 200, trimmed); flags exactly
  `true`/`false`; section lists must name every section exactly once with no unknown ids; price sections must
  be selectable, unique and canonical; versions must have valid GUIDs, counters ≥ 1, ≤ 64 entries. A bad value
  is dropped individually; more than 50 bad values rejects the whole file.

## Older-client compatibility

RC.9/RC.10-era builds enumerate only their known folders, so they never read, write or delete
`morning-message/`, do not count it as malformed, and keep syncing drafts, notes, History, ERT and price
history (tested with an RC.9-equivalent `PersonalSyncService` that has no Morning Message channel, including
one whose config file carries the new flag). The manifest is byte-for-byte unchanged. If an older build
re-saves `sync-config.json` it drops the unknown flag — on downgrade the option simply reads Off again.

## Database / schema decisions

No database migration: DB stays **17**. Draft 4, Snapshot 2 and `SyncSchemaVersion` 1 unchanged. Morning
Message content stays in its Phase 29 local JSON files; the sync bookkeeping is one more local JSON file. Live
SQLite files are untouched and never in the sync folder.

## Privacy

- Message text (associate names, break assignments, staffing notes) leaves this computer only as files in the
  user-selected Personal Sync folder. No other server, no telemetry.
- Logs carry counts, file names and exception types only — never message text (tested with marker text in
  valid, conflicting and malformed files).
- Diagnostics export is unchanged and does not include the new files.
- No real associate data is used anywhere in tests or docs.

## Files

| Layer | Files |
| --- | --- |
| Application | `MorningMessage/Sync/MorningMessageVersion.cs`, `MorningMessageSyncFields.cs`, `MorningMessageFieldReconciliation.cs`, `MorningMessageSyncContracts.cs` (result, status, conflict, service interfaces, `MorningMessageEditorSession`); `MorningMessagePreferences.cs` (store interface: `LoadAllAsync`, `SaveManyAsync`, cleared-date contract); `PersonalSync/SyncConfiguration.cs` (`MorningMessageSyncEnabled`), `SyncPathService.cs`, `IPersonalSyncService.cs` (`PersonalSyncResult.MorningMessages`) |
| Infrastructure | `PersonalSync/MorningMessageSync.cs` (channel + settings service), `MorningMessageSyncFile.cs` (format, validation), `MorningMessageSyncStateStore.cs`; `PersonalSyncService.cs` (runs the channel last), `SyncTempFileCleaner.cs`; `Preferences/FileMorningMessageStores.cs` |
| Desktop | `ViewModels/MorningMessageSyncSettingsViewModel.cs`, `PersonalSyncSettingsViewModel.cs`, `MorningMessageViewModel.cs`, `AssociateViewModel.cs`; `Presentation/MorningMessageSyncPresentation.cs`, `PersonalSyncResultPresentation.cs`; `Views/PersonalSyncSettingsView.axaml`, `MorningMessageView.axaml`; `App.axaml.cs` (DI, composer → sync trigger) |
| Documents | unchanged (no PDF, Teams formatting or plain-text change) |

## Tests

Deterministic suite (Release, `Category!=LiveSmoke`, the five real-Window UI classes run alone):
**4,949 passed, 0 failed** (baseline 4,838; **+111**).

| Project | Count | Change |
| --- | --- | --- |
| Domain | 303 | — |
| Application | 1,330 | +36 `MorningMessageSyncRulesTests` (version vectors, encodings, validation, merge rule, session) |
| Infrastructure | 1,136 | +40 `Phase30MorningMessageSyncTests`, +6 `Phase30PersonalSyncCompatibilityTests`, +1 net in `FileMorningMessageStoresTests` |
| Documents | 525 | — |
| Ui (mixed) | 1,503 | +28 `Phase30MorningMessageSyncUiTests` |
| Ui run-alone classes | 26 + 113 + 2 + 7 + 4 | — |

Release build (`dotnet build CarowindsPassdown.slnx -c Release`, full rebuild): **0 warnings, 0 errors.**

One existing test changed by design: `FileMorningMessageStoresTests.Content_SavingEmpty_RemovesTheDate`
became `Content_ClearingADate_KeepsAnEmptyEntry_SoSyncCanTellClearedFromAgedOut`, plus a new
`Content_SavingEmptyForANeverWrittenDate_WritesNothing`. No other test expectation changed.

Coverage against the Part H list (all **PASS BY TEST**, synthetic data):

- Enable/disable: off by default (incl. pre-Phase 30 config file); persistence across restart; other Personal
  Sync settings preserved; A on / B off; B enabled later; disable deletes nothing (local, shared, state); disable
  → edit → other edits → re-enable → conflict, nothing lost.
- Daily content: every field; multiple dates; empty vs missing; multiline; Unicode/emoji/CJK; `& < > " '`;
  60-date retention without ping-pong; DAP image path never published.
- Conflicts: identical; different; independent fields; same field; different section orders (preferences);
  first-time enablement 30 + 30 days; offline concurrent editing; repeated import; import after restart;
  three-device convergence (independent edits, and a three-way same-field disagreement); a conflict resolved on
  one computer adopted by the other (both choices); lost local sync state.
- Corruption/compatibility: truncated JSON; non-JSON; oversized; newer format version; other format; device-id
  mismatch; too many dates; too many bad values; unexpected section ids; invalid dates and path-like keys;
  over-long text; invalid versions; missing fields; leftover temp file (interrupted write); provider conflict
  copy; unwritable folder and unreadable (locked) file (Unix permissions — Windows locking is on the VM
  checklist); unreadable local file never replaced; RC.9-equivalent peer in both directions; manifest and
  `SyncSchemaVersion` unchanged; sync order independence.
- Composer: date held while open; notice shown; typed text never replaced; held changes applied after close; an
  edit made meanwhile becomes a conflict; preferences/text reloaded on open; Copy menu follows the synced copy
  mode; date switching moves the hold; OD Notes stay out of the main message with synced content; resolve refused
  while open.
- Settings: off and unavailable without a folder; toggling saves and requests a sync; every status line; conflict
  review and both resolutions; composer-open refusal message; parent screen refreshes the line; markup bindings
  and exact wording.
- Regression: the full existing suite passes unchanged, including draft/note/History/ERT sync, official price
  history sync, legacy Season Pass identity folding, current pricing, PDF output, Frozen History, Morning
  Message formatting, plain-text fallback, OD Notes separation, save-failure protection, the three original Copy
  actions, updater and migration tests.

A deliberate mutation of the merge rule (always take the other computer's value) made 10 of the multi-computer
tests fail, confirming they detect silent overwrites.

## Windows validation — NOT EXECUTED

No Windows host was available to this session (macOS build host). Every row below is **NOT EXECUTED** until
run on the Windows test VM with two disposable profiles (as in Phase 29A) and synthetic text only — never real
associate names.

| # | Check | Result |
| --- | --- | --- |
| 1 | A and B point to the same empty test sync folder; both enable Sync Morning Messages; A types greeting, reminders, break plan, OD Notes, price note for two dates → Sync Now on A, then B → B shows them; status "Synced" on both | NOT EXECUTED |
| 2 | Section order, section visibility, price sections, bullets, copy mode changed on A → appear on B | NOT EXECUTED |
| 3 | A enabled, B disabled → B's messages unchanged, no `morning-message/<B>.json`; B enabled later joins | NOT EXECUTED |
| 4 | A disabled after syncing → shared files and A's messages unchanged | NOT EXECUTED |
| 5 | A disabled, edits reminders; B edits the same reminders; A re-enabled → "Conflict needs review"; both texts shown; Keep This Device / Use Other Device each tested; the other computer adopts it | NOT EXECUTED |
| 6 | Composer open on B while A changes that date → notice appears, typed text kept; close → change arrives or conflict shown | NOT EXECUTED |
| 7 | OneDrive paused / folder renamed → "Shared folder unavailable", editing still works; resumed → syncs | NOT EXECUTED |
| 8 | A file locked by another process (e.g. open in an editor with a lock) → that pass skips it, next pass succeeds | NOT EXECUTED |
| 9 | Prices on B come from B's own catalog (change nothing official on A and confirm B's prices/freshness notes are B's) | NOT EXECUTED |
| 10 | Teams: synced message → Copy Formatted and Copy Plain Text paste correctly; OD Notes absent from the main message, Copy OD Notes separate | NOT EXECUTED (Teams not tested) |
| 11 | An RC.9 build on a third profile sharing the folder keeps syncing drafts/notes and leaves `morning-message/` alone | NOT EXECUTED |

Evidence to record per row: Windows build, app build (`About this build`), test profile, test sync directory,
before/after text, conflicts shown, recovery result; screenshots only with synthetic text.

## Known limitations

1. Windows and Teams behavior are not validated (above). Unix permissions stand in for Windows file locks in
   the automated tests.
2. Resolution is per record (a date, or the preferences): Keep/Use applies to every conflicting part of that
   record. Non-conflicting parts were already merged and are not affected.
3. With three or more computers that all disagree on one field, conflicts are presented one other value at a
   time (each needs its own choice).
4. A date that ages out of one computer's 60 dates is not re-imported there, even if another computer still
   keeps it.
5. The DAP image itself never synchronizes (by design; only the attach-image reminder does).
6. Conflict review lives in Settings → Personal Sync; the composer shows only the waiting notice.
7. Downgrading to a pre-Phase 30 build turns the option off on that computer (its config writer drops the flag);
   shared files remain.
8. A local `morning-message-content.json` that cannot be read blocks Morning Message sync on that computer
   (deliberately — it is never overwritten) until the file is repaired or removed.

## Next release recommendation

Phase 30 is ready for a packaged candidate after the Windows two-profile checklist above (and the still-open
Phase 29A Teams rows) pass on the VM. Package as the next RC with its own version bump and release
preparation — not with `Build-PortableRelease.ps1` at the current `0.1.0-rc.9` version, which would overwrite
the frozen RC.9 output.
