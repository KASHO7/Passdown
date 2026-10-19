# Installing Carowinds Operations 0.1.0-rc.8

A plain-language guide for installing RC.8 on a Windows 11 PC. No technical background is needed.

> RC.8 is a **test version**. It has not yet been checked on Windows and is not approved for wider
> distribution. Use it only if you were asked to test it.

You need the file **`Carowinds-Operations-0.1.0-rc.8-win-x64.zip`**. Its exact size and checksum are
listed in `docs/release-manifest.md` "0.1.0-rc.8" — the person who gave you the file can confirm them.

## 1. Back up your existing app data

Your passdowns, notes, History, prices and settings are stored separately from the app, in
`%LOCALAPPDATA%\Carowinds Passdown`. Copy that folder before you start:

1. Close Carowinds Operations completely (check the taskbar).
2. Avoid doing this around 6:00 AM, when the automatic daily refresh may be running.
3. Press **Windows + R**, type `%LOCALAPPDATA%` and press **Enter**.
4. Right-click the **Carowinds Passdown** folder → **Copy**, then paste it somewhere else, such as your
   Documents folder.
5. Rename the copy with today's date, for example `Carowinds Passdown backup 2026-10-18`.

Never copy a backup back while the app is open. If you use Personal Sync on several computers, plan to
upgrade all of them together.

## 2. Extract the RC.8 ZIP

1. Right-click the ZIP → **Extract All…**
2. Choose a **new, empty folder you can write to**, such as `Documents\Carowinds Operations rc.8`.
   Don't use `C:\Program Files` — the app couldn't update itself there later.
3. Don't extract on top of an older version's folder, and don't run the app from inside the ZIP.

## 3. Launch Carowinds Operations

Open the extracted folder and double-click **`Carowinds.Passdown.Desktop.exe`**. Nothing else needs
installing (no .NET, no administrator rights).

If Windows shows **"Windows protected your PC"**, click **More info** → **Run anyway**, but only if the ZIP
came from the person who asked you to test.

## 4. Check that it says RC.8

Click the **gear** (Settings) at the right of the navigation bar and scroll to **About this build**. It
should say **Version 0.1.0-rc.8 (a651e26)** and show the folder you just extracted. If it shows another
version, you opened an older copy — close it and start the one in the new folder.

## 5. Try the four main changes

1. **Safer saving** — the Passdown screen now shows **"Save failed — changes not saved"** and a **Retry
   Save** button if a save ever fails, and the app no longer freezes. (Only test a failed save on a test
   copy of your data, following `TESTER-GUIDE.txt` step 15.)
2. **Dashboard Shows** — on a day with lots of shows, times and venues no longer overlap; **+N more**
   shows every time. The banner shows just "CAROWINDS OPERATIONS".
3. **Appearance** — Settings → **Appearance**: pick **Use Windows setting**, **Light** or **Dark**. It
   changes immediately and is remembered after you restart.
4. **Updates** — see steps 6–8.

## 6. Find Check for Updates

Settings → **Application Updates** → **Check for Updates**. Right now it will most likely say you are up
to date or that it couldn't check — that's expected, because no newer version has been approved or
published. **Don't install anything from here** unless the person who asked you to test gave you a special
test update.

## 7. Why this first RC.8 install is manual

The update feature is new in RC.8. RC.7 and earlier don't have it, so they can't update themselves to
RC.8 — that's why you extracted the ZIP by hand. Because this was a manual install, What's New does
**not** open by itself on first launch.

## 8. How future updates are meant to work

This is how it's designed to work. It will only be relied on after it has passed testing on Windows:

1. When a newer version has been approved and published, a small **dot** appears on the Settings gear
   (no pop-ups).
2. Settings → Application Updates shows the version and its notes. Choose **Install Update**.
3. When the download has been checked, choose **Restart and Install**. Your unsaved work is saved first;
   if it can't be saved, the update waits.
4. The app closes, updates and opens again. Your data is kept.
5. If the new version doesn't start, the previous version is put back and reopened.

Nothing downloads or installs unless you click those buttons.

## 9. Find What's New

Settings → **About this build** → **What's New** shows the notes for the version you're running, even
without internet. After a successful in-app update, it opens once by itself.

## 10. If something goes wrong

1. Write down what you did, what you expected and what happened.
2. Take a screenshot (**Windows + Shift + S**).
3. Settings → **Diagnostics** → **Export Diagnostics…** and save the ZIP. It contains version and health
   information and recent logs, never your drafts, notes, PDFs or database.
4. Send all of that to the person who asked you to test.
5. Don't delete anything in `%LOCALAPPDATA%\Carowinds Passdown` — that's your saved data. To go back to an
   older version, close the app, put your dated backup folder back in place, then run the older version.
