Carowinds Passdown
==================

What this is
-------------
A portable Windows app. No installer, no admin rights, nothing else to install —
.NET is already bundled inside this folder.

Requirements
------------
Windows 11, 64-bit.

Getting started
----------------
1. Extract the ZIP file anywhere you like (Desktop, Documents, a USB drive — anywhere
   you can write files).
2. Open the extracted folder.
3. Double-click Carowinds.Passdown.Desktop.exe to launch the app.

That's it — no setup screen, no sign-in, no separate download.

Where your data is stored
--------------------------
Everything you enter (drafts, cached park data) is stored on your own computer at:

    %LOCALAPPDATA%\Carowinds Passdown

This is a per-user folder outside the app folder you extracted. It is created the
first time you run the app.

Logs live at:

    %LOCALAPPDATA%\Carowinds Passdown\logs

New to testing this app? See TESTER-GUIDE.txt in this same folder for a short
step-by-step walkthrough.

Passdown History
-----------------
Next to "Associate Passdown" at the top of the window is a "History" section. Each
day's passdown finalizes automatically the morning after it happens (after 6:00 AM
the following day) and is stored exactly as associates saw it, including the exact
PDF — nothing in History can be edited or deleted, and nothing is ever regenerated.
Use it to look up or re-print an exact past passdown at any time.

Something not working?
------------------------
Click "Export Diagnostics" inside the app (next to "Create PDF") and save the
resulting ZIP file. It contains version/health information and recent activity logs
only — never your drafts, notes, or saved database — and is the fastest way to get
help from whoever supports this app.

Automatic daily refresh (optional)
------------------------------------
The app refreshes park data automatically whenever it's open. If you'd also like it
to refresh once a day even when the app isn't running, double-click:

    Enable Automatic Refresh.cmd

Extracting the ZIP does NOT set this up automatically — it's opt-in. Double-clicking
this file sets it up for THIS extracted copy of the app; nothing to type, no
PowerShell window to configure. A window will briefly open, show whether it worked,
and wait for you to press a key before closing.

To turn it back off later, double-click:

    Disable Automatic Refresh.cmd

No admin rights are needed for either one, and neither changes any Windows security
setting on your computer — see "How the setup helpers work" below if you're curious.

Moving this folder
-------------------
If you move or copy this extracted folder somewhere else (a different drive, a new
USB stick, etc.) and you had automatic refresh enabled, double-click
"Enable Automatic Refresh.cmd" again from the new location. It safely replaces the
old schedule with one that points at the new location — you don't need to disable
the old one first.

Removing the app
-----------------
1. If you enabled automatic refresh, double-click "Disable Automatic Refresh.cmd"
   first (safe to run even if you're not sure it's enabled).
2. Close the app if it's open.
3. Delete the extracted folder. There is no separate uninstaller.

Deleting the folder does NOT delete your data — your drafts and cached park data
stay at %LOCALAPPDATA%\Carowinds Passdown until you delete that folder yourself. If
you also want to remove your saved data, delete that folder too. Deleting the app
folder by itself does not remove a scheduled refresh you set up — disable it first
as described above.

How the setup helpers work
-----------------------------
"Enable Automatic Refresh.cmd" and "Disable Automatic Refresh.cmd" are the
nontechnical entry points; behind the scenes they call the scripts in the "tools"
folder (tools\Install-ScheduledRefresh.ps1 / tools\Uninstall-ScheduledRefresh.ps1),
using the exact folder you extracted — you never need to type a file path. If your
copy of Windows normally blocks PowerShell scripts from running, these helpers avoid
that prompt for themselves only, for this one action; they do not change any
script-running setting on your computer, permanently or otherwise. If you prefer to
run the underlying scripts directly, they're still there in "tools" and accept the
same options as before.
