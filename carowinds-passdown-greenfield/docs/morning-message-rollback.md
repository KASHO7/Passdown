# Decision record — Morning Message Builder retired (targeted rollback)

Date: 2026-10-23. Branch `feature/passdown-redesign`. Rollback of Phases 29, 29A, 29B, 29C and 30 on top of
`827b686`, as a new commit (no history rewritten, nothing pushed). Version stays `0.1.0-rc.9`.

## Why

The Morning Message Builder's main purpose was a formatted Teams/Word message. On the Windows 11 test VM,
formatted copies kept losing bold in both Word and Teams desktop across three fix attempts (29A validation,
29B markup changes, 29C native clipboard writer, which was never validated). The product owner decided not to
keep maintaining the feature. The pre-Phase-29 plain-text Copy options cover the daily need.

## Removed

| Area | What |
| --- | --- |
| Associate | Copy ▾ → Morning Message… and OD Notes. The Copy menu is back to its three original items |
| Composer | Morning Message overlay: greeting, section enable/order, DAP/reminders/break plan/price note/OD Notes inputs, styled preview, Copy Formatted / Copy Plain Text / Copy Image, "Use formatted copy by default" |
| Formatted copy | `TeamsMessage` model, HTML renderer, CF_HTML builder, preview renderer, `TeamsClipboard`, the HTML/image clipboard methods, the Phase 29C native Windows clipboard writer |
| Persistence | Morning Message preference and per-date content stores (`morning-message.json`, `morning-message-content.json`) |
| Sync | Settings → Personal Sync → Sync Morning Messages, the `morning-message/` channel, field-level merge, conflict review, `morning-message-sync-state.json` bookkeeping, `SyncConfiguration.MorningMessageSyncEnabled`, `PersonalSyncResult.MorningMessages` |
| Tooling | Phase 29A, 29+30, 29B and 29C Windows kits (`tools/phase29*-windows-kit/`), the Morning Message visual-fixture scenarios, the `.gitattributes` rule for the kit's byte-exact files |
| Tests | Morning Message composer, rendering, CF_HTML, clipboard, store, sync and UI tests |

Method: every file under `src/` and `tests/` is restored to RC.9's source commit `780ceac` (the last commit before
Phase 29). Files added since then are deleted. The one exception is the release-guard test below. After the
rollback, `git diff 780ceac -- src` is empty.

## Preserved

Everything that existed before Phase 29 is unchanged: Associate Passdown, the three original Copy options
(byte-identical plain text, guarded by `AssociateTeamsTextFormatterTests`, `AssociateViewModelCopyForTeamsTests`
and the new `MorningMessageRetiredTests`), Season Pass and Membership pricing, Price History with legacy
identity folding (`780ceac` itself), Official Price History Sync, Draft/Notes/History/ERT sync, Dashboard,
Calendar, Notes, History, PDF, updater, backups and recovery, and appearance settings.

**Release-script guard retained.** `packaging/windows/Build-PortableRelease.ps1` (refuses to run when the frozen
`Carowinds-Operations-<Version>-win-x64.zip` exists; `-LocalTestLabel` builds go to `artifacts/local-test/`) and
its `PortablePackagingDataSafetyTests` were added during Phase 29+30 work but are not Morning Message
functionality. While the version is still `0.1.0-rc.9`, the guard is what stops a test build from deleting or
overwriting the frozen RC.9 release, so removing it would reintroduce a real data-loss hazard for release
artifacts. Its comments still cite the Phase 29A finding that motivated it; that is history, not a dependency.

## Existing data

Nothing is deleted or migrated.

- **Local files** in `%LOCALAPPDATA%\Carowinds Passdown\`: `morning-message.json`, `morning-message-content.json`,
  `morning-message-sync-state.json`. They are left on disk as unused legacy data. No code reads, writes or deletes
  them (`RetiredMorningMessageCompatibilityTests.LocalMorningMessageFiles_AreLeftOnDisk_Unchanged_ThroughSyncPasses`).
- **`sync-config.json`** written by a Phase 30 build may contain `"MorningMessageSyncEnabled"`. It loads normally:
  the unknown property is ignored and Personal Sync stays on with the same folder. The field is dropped the next
  time this computer saves its sync settings (`SyncConfigWrittenByAPhase30Build_StillLoads_AndSyncRuns`).
- **Shared folder**: `CarowindsPassdownSync/morning-message/` files (and conflict copies or temp files there) stay
  where they are. Personal Sync enumerates only its known folders, as RC.9 always did, and its temp cleaner is
  folder-scoped, so they are never read, written, cleaned or re-created. Drafts, notes, History, ERT and
  official price history keep syncing
  (`SharedFolderWithRetiredMorningMessageFiles_StillSyncsNotesAndPriceHistory_AndLeavesThoseFilesUntouched`).
  A user may delete that folder by hand; the app never will.

## Compatibility

- DB schema 17, Draft 4, Snapshot 2, `SyncSchemaVersion` 1: unchanged. Phase 29/30 never changed them, so there
  is no migration and no downgrade concern.
- Mixed computers: a Phase 30 test build still on another computer keeps writing its own
  `morning-message/<device-id>.json`. This build ignores it, and every other entity syncs as before.
- RC.9 and this build behave identically on every shared folder.
- The historical Phase 29/29A/29B/29C/30 and 29+30 kit documents are kept unchanged below a "Retired" banner.

## Verification

- Deterministic suite (Release, `Category!=LiveSmoke`, five real-Window classes alone): 303 Domain + 1286
  Application + 1079 Infrastructure + 469 Documents + 1463 Ui mixed + 26 + 113 + 2 + 7 + 4 = **4752 passed,
  0 failed**. That is the RC.9 fold baseline 4736, plus 2 kept release-guard tests, plus 14 rollback tests
  (`MorningMessageRetiredTests` 11, `RetiredMorningMessageCompatibilityTests` 3).
- Clean Release build (`dotnet clean` + `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`):
  0 warnings, 0 errors. `tools/visual-fixtures` builds.
- `git diff 780ceac -- src` is empty. No Morning Message type in any assembly, no Morning Message markup in any
  Desktop view, and the Copy menu has exactly its three original items.
- Not validated on Windows. The restored source is RC.9's, which was validated on Windows for RC.9.
