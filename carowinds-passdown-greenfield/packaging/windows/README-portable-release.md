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

Automatic daily refresh (optional)
------------------------------------
The app refreshes park data automatically whenever it's open. If you'd also like it
to refresh once a day even when the app isn't running, two optional helper scripts
are included in the "tools" folder inside this release:

    tools\Install-ScheduledRefresh.ps1
    tools\Uninstall-ScheduledRefresh.ps1

Extracting the ZIP does NOT set this up automatically — it's opt-in. To enable it,
open PowerShell in the extracted folder and run:

    tools\Install-ScheduledRefresh.ps1 -ExecutablePath "<full path to Carowinds.Passdown.Desktop.exe in this folder>"

To turn it back off later, run:

    tools\Uninstall-ScheduledRefresh.ps1

No admin rights are needed for either script.

Removing the app
-----------------
Just delete the extracted folder. There is no separate uninstaller.

Deleting the folder does NOT delete your data — your drafts and cached park data
stay at %LOCALAPPDATA%\Carowinds Passdown until you delete that folder yourself. If
you also want to remove your saved data, delete that folder too.

If you enabled the automatic daily refresh above, run
tools\Uninstall-ScheduledRefresh.ps1 before (or after) deleting the folder — deleting
the folder by itself does not remove the scheduled task.
