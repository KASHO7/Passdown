# Carowinds Operations 0.1.0-rc.9 — Release notes

**Test version.** RC.9 has been built and checked on the build computer only. It has **not** been
tested on Windows yet and is **not approved** for wider distribution. Exact file sizes and checksums:
`docs/release-manifest.md` "0.1.0-rc.9". Installation: `docs/releases/rc9-installation-guide.md`.

RC.9 contains everything in RC.8 (safer saving with Retry Save, tidier Dashboard Shows, the title-only
Fury 325 banner, Use Windows setting / Light / Dark, in-app updates from approved GitHub releases, What's
New) plus the changes below.

## Official pricing

- **Season Pass prices work again.** The Carowinds Memberships & Season Passes page was redesigned;
  the app reads the new page again.
- **Silver, Gold and Prestige Season Passes stay separate**, each with its own price.
- **Memberships are new.** Each Membership is its own product. Monthly prices are shown per month
  (for example "$7.00/month") and are never added up into a yearly total.
- **Initiation fees** (one-time) are listed separately from the monthly price.
- **Renewal offers** are shown separately from new-purchase prices, and a "reference" or "was" price on
  the website is never taken as the current price.
- If the website doesn't show a Membership, the app says so — it never records it as $0.
- If only part of the page can be read, the prices that couldn't be read keep their last verified
  value and say so. Nothing is guessed.
- **Your existing price history is kept exactly as it was.**

## Price History Sync

- If you use Personal Sync, verified official price history now travels between your computers through
  the same sync folder (in a new `official-price-history` folder inside it).
- History from each computer is **added together**. Nothing is overwritten or deleted, and syncing
  again never creates duplicates.
- Each record keeps the date and source it was originally seen with.
- The **current price** on each computer still comes only from that computer's own official checks.
  Shared history never changes what the app shows as today's price.
- Drafts, notes and History keep syncing exactly as before.
- **Every computer needs RC.9 to share price history.** An RC.8 computer can keep using the same sync
  folder for drafts and notes; it simply ignores the price history (and leaves those files alone).

## Source links

- Associate → Products now has **View Official Memberships & Season Passes Page**, which opens the
  official page in your web browser. Price History offers the same link for a product when this
  computer recorded where the price came from. Only official sixflags.com addresses are ever opened.

## Important upgrade information

- **The database is upgraded (format 16 → 17)** the first time RC.9 starts. The upgrade only adds a new
  table for shared price history; nothing you already have is changed.
- **A backup is made first, automatically.** Before upgrading, RC.9 writes a checked copy of your
  database to `%LOCALAPPDATA%\Carowinds Passdown\backups\`. If that copy can't be made (for example the
  disk is full), RC.9 does not upgrade anything and your data stays readable by RC.8.
- **When updating from inside RC.8**, the updater also makes and checks a backup *before* it replaces
  any program file. If it can't, nothing is installed.
- **Older versions cannot open the upgraded database.** RC.8 refuses to start with it (it never changes
  or damages it). Going back to RC.8 means restoring the backup, and anything saved after the upgrade
  would not be in that backup. See the installation guide, "If the update fails".
- **If RC.9 upgrades the database but then fails to start properly during an in-app update**, the
  updater does **not** put RC.8 back (RC.8 couldn't open the database). It keeps RC.9, your database,
  the backup and the RC.8 program files, and writes `UPDATE-RECOVERY.txt` in the app folder explaining
  what to do. It never restores or deletes a database by itself.
- **Upgrade every computer** that shares a Personal Sync folder, one at a time, to share price history.
- **In-app update from RC.8 — status: prepared, not yet validated on Windows.** RC.8 hands the
  installation to the updater shipped inside RC.9, so the safety steps above do apply to an RC.8 → RC.9
  update. This has been tested with synthetic data on the build computer only. Until it passes the real
  Windows test (`docs/releases/rc9-windows-validation.md` §8), install RC.9 **manually from the ZIP**.
  RC.9 has not been published on GitHub, so RC.8 will not offer it.

## Not in this version

Ride Schedule History, Special Events, Memberships in PDF exports, changes to the Daily Feature math,
SharePoint updates and a Windows installer are not part of RC.9. The Manager Passdown remains deferred.
