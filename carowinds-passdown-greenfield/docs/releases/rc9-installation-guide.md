# Installing Carowinds Operations 0.1.0-rc.9

A plain-language guide for installing RC.9 on Windows 11 PCs. No technical background is needed.

> RC.9 is a **test version**. It has not yet been checked on Windows and is not approved for wider
> distribution. Use it only if you were asked to test it, and only on a computer whose data you have
> backed up (step 2).

You need **`Carowinds-Operations-0.1.0-rc.9-win-x64.zip`**. Its exact size and checksum are in
`docs/release-manifest.md` "0.1.0-rc.9" (75,892,218 bytes, SHA-256 begins `e1ad521f`).

## 1. Why RC.9 is a bigger update than usual

RC.9 adds shared price history. To store prices that come from your other computers, it adds a new
table to the app's database the first time it starts. This is called a **database upgrade** (format 16
→ 17). Nothing you already have is changed: your drafts, notes, History PDFs, prices and settings stay
exactly as they are.

The catch: **RC.8 and older versions cannot open an upgraded database.** Once RC.9 has started on a
computer, going back to RC.8 means putting an older copy of the database back. That is why backups
matter for this version.

## 2. Make your own backup first

RC.9 makes its own backup automatically (step 4), but your own dated copy is the safest way back.

1. Close Carowinds Operations completely (check the taskbar).
2. Avoid doing this around 6:00 AM, when the automatic daily refresh may be running.
3. Press **Windows + R**, type `%LOCALAPPDATA%` and press **Enter**.
4. Right-click the **Carowinds Passdown** folder → **Copy**, then paste it somewhere else, such as your
   Documents folder.
5. Rename the copy with today's date, for example `Carowinds Passdown backup 2026-10-22`.
6. Open the copy and check that `carowinds-passdown.db` is there and isn't 0 KB.

Never copy a backup back while the app is open.

**Several computers?** If you use Personal Sync, make this backup on **each** computer, and also copy
the shared sync folder somewhere safe. Before upgrading, open the app on each computer, choose
**Settings → Personal Sync → Sync Now**, and wait until "Last synced" updates, so nothing is waiting to
be shared.

## 3. Install RC.9 from the ZIP

**Use the ZIP for RC.9.** RC.8's in-app **Install Update** is designed to handle this upgrade safely (see
step 9), but that has not been tested on Windows yet, and RC.9 has not been published on GitHub, so RC.8
won't offer it.

1. Right-click the ZIP → **Extract All…**
2. Choose a **new, empty folder you can write to**, such as `Documents\Carowinds Operations rc.9`.
   Don't use `C:\Program Files`, and don't extract on top of the RC.8 folder.
3. Keep the RC.8 folder for now. **Don't start RC.8 again** on this computer after RC.9 has started
   (it will refuse to open the upgraded database).
4. Open the new folder and double-click **`Carowinds.Passdown.Desktop.exe`**. Nothing else needs
   installing. If Windows shows **"Windows protected your PC"**, click **More info** → **Run anyway**,
   but only if the ZIP came from the person who asked you to test.

## 4. What happens the first time RC.9 starts

1. RC.9 sees that the database is in the RC.8 format.
2. It saves a **checked copy** of the database in
   `%LOCALAPPDATA%\Carowinds Passdown\backups\`, named like
   `carowinds-passdown-pre-migration-schema-16-to-17-<date>-<time>Z-<code>.db`. "Checked" means it
   opened the copy and confirmed it is complete and undamaged, with the same records as the original.
3. Only then does it add the new table. Then it checks the whole database again.
4. The window opens as usual.

If the copy can't be made (for example the disk is full), RC.9 **doesn't upgrade anything** and closes.
Your database stays in the RC.8 format. Free some space and start RC.9 again. Backups are never deleted
automatically; once RC.9 has worked well for a while you can delete old ones yourself.

## 5. Check the updated app

1. **Settings** (gear) → **About this build** should say **Version 0.1.0-rc.9 (e20d846)** and show the
   folder you just extracted.
2. **History**: your older finalized passdowns are still there; **Open Final PDF** opens them.
3. **Passdown**: open a date you had a draft for — your text is still there.
4. **Notes**: your notes are still there.
5. **Settings → Appearance**: still your choice. **Settings → Personal Sync**: still set up.
6. **Passdown → Products** (after **Check Online**): Silver, Gold and Prestige Season Passes appear
   separately; Memberships show "/month" prices and a separate initiation fee; renewal offers are
   labelled as renewals. **View Official Memberships & Season Passes Page** opens the official page in
   your browser. **More ▾ → Season Price History** still shows the prices you had before.
7. Check that the backup file from step 4 exists in the `backups` folder.

## 6. Price history on several computers

- Price history is shared through your **existing** Personal Sync folder. You don't need to set
  anything up.
- **Upgrade one computer first**, check it (step 5), then upgrade the others one at a time. Upgrading
  one computer does not upgrade the others.
- After each upgraded computer syncs, its official price history is added to the others. Each record
  keeps its original date and source; nothing is overwritten or deleted, and syncing again never
  creates duplicates.
- After a sync that brought in new history, the sync status says so, for example "Official price
  history: 28 new observations merged." Syncing again doesn't repeat it.
- Each computer's **current** price still comes only from its own **Check Online** — shared history
  never changes it.
- While some computers still run RC.8, they keep syncing drafts and notes normally and just ignore the
  price history (they don't delete it).
- When all computers are upgraded and have synced, open **Season Price History** on two of them and
  check that they show the same history, with no duplicated lines.

## 7. If something goes wrong after a manual install

- **RC.9 closes straight away the first time:** start it once more. If it still closes, don't start
  RC.8. Look in `%LOCALAPPDATA%\Carowinds Passdown\logs` for today's `app-…log`, and send it (with
  what you saw) to the person who asked you to test. If the log says the backup could not be made,
  free disk space and try again — nothing was changed.
- **Going back to RC.8** (only if you really need to, and preferably with help):
  1. Close Carowinds Operations completely.
  2. In `%LOCALAPPDATA%\Carowinds Passdown`, **move** (don't delete) `carowinds-passdown.db` — and any
     `carowinds-passdown.db-journal`, `-wal` or `-shm` files — into a new folder such as
     `set aside 2026-10-22`. They hold anything you did in RC.9.
  3. Copy your backup into `%LOCALAPPDATA%\Carowinds Passdown` and name it `carowinds-passdown.db`.
     Use either your own dated backup (step 2) or the `…pre-migration-schema-16-to-17…db` file from the
     `backups` folder.
  4. Start RC.8 from its old folder.

  Anything you typed or saved while using RC.9 is **not** in that backup. It's still in the set-aside
  copy, which RC.9 can open again later.

## 8. What's New

- After a manual ZIP install (steps 3–4), What's New does **not** open by itself. That's expected.
- Open it any time: **Settings → About this build → What's New**. It works without internet.
- After a successful **in-app** update it opens once by itself, and not again on later restarts. It
  never opens after a failed update.

## 9. In-app update from RC.8 (for testers asked to try it)

This is how the RC.8 → RC.9 in-app update is designed to work. It has only been tested with synthetic
data on the build computer, so only try it with a **backed-up or disposable test profile** and a
special test release given to you for that purpose (`docs/releases/rc9-windows-validation.md` §8).

1. RC.8 shows RC.9 under **Settings → Application Updates**. Before you choose **Install Update**, its
   notes start with: *"RC.9 updates the local database. A backup is required before installation. Older
   versions cannot open the updated database."*
2. **Install Update** downloads and checks the signed package. **Restart and Install** saves your work
   (if it can't, the update waits) and closes RC.8.
3. The updater (which comes from inside the RC.9 package) makes and checks a backup of the database,
   named `…pre-update-0.1.0-rc.8-to-0.1.0-rc.9-…db`, **before** it replaces any file. If the backup
   can't be made or checked, nothing is installed; start RC.8 again and it explains why.
4. It replaces the program files and starts RC.9. RC.9 makes its own pre-upgrade copy, upgrades the
   database, checks it, and opens its window. Only then is the update counted as successful, and What's
   New opens once.
5. **If RC.9 fails before it upgrades the database**, the updater puts RC.8 back and starts it once.
6. **If RC.9 fails after it upgraded the database**, the updater does **not** put RC.8 back (RC.8
   couldn't open the database) and does not restore any backup by itself. It keeps RC.9, your database,
   both backups and the RC.8 program files, and writes **`UPDATE-RECOVERY.txt`** in the app folder
   listing where each one is. Start RC.9 again first — if it opens, the update worked. If it doesn't,
   follow step 7 ("Going back to RC.8"); the RC.8 program files are in the folder that
   `UPDATE-RECOVERY.txt` names.

## 10. Reporting a problem

1. Write down what you did, what you expected and what happened.
2. Take a screenshot (**Windows + Shift + S**).
3. **Settings → Diagnostics → Export Diagnostics…** and save the ZIP (it never contains your drafts,
   notes, PDFs or database).
4. Send that, plus `UPDATE-RECOVERY.txt` if there is one, to the person who asked you to test.
5. Don't delete anything in `%LOCALAPPDATA%\Carowinds Passdown`, including the `backups` folder.
