#!/usr/bin/env bash
# Phase 27 offline updater test kit (docs/releases/rc9-updater-test-harness.md §8). Build-host only; uploads nothing.
#
#   build-offline-kit.sh <work-dir outside the repository>
#
# Needs the frozen test releases from tools/updater-test-harness/build-updater-test-releases.sh
# (artifacts/updater-test/). Produces artifacts/updater-test-offline/kit/ (git-ignored, read-only) for private
# delivery to the Windows test machine:
#   LOCAL-ONLY-updater-test-start-app-…-0.0.900-rc.1-win-x64.zip   the starting app (unchanged copy)
#   packages/v0.0.900-rc.N/                                          the five signed test releases (unchanged copies)
#   LOCAL-ONLY-offline-update-stager-win-x64.zip                     the stager, built from RC.9 source + test key
#   SHA256SUMS.txt
set -euo pipefail

WORK=$1
REPO=$(cd "$(dirname "$0")/../.." && pwd)
GITROOT=$(git -C "$REPO" rev-parse --show-toplevel)
SUBDIR=${REPO#"$GITROOT"/}
TESTREL="$REPO/artifacts/updater-test"
OUT="$REPO/artifacts/updater-test-offline"
KIT="$OUT/kit"
RC9_SOURCE=e20d846

mkdir -p "$WORK"; WORK=$(cd "$WORK" && pwd)
case "$WORK" in "$GITROOT"*) echo "Work directory must be outside the repository." >&2; exit 1;; esac
[[ -e "$OUT" ]] && { echo "$OUT already exists; the kit is immutable. Move it aside first." >&2; exit 1; }
(cd "$TESTREL" && shasum -a 256 -c SHA256SUMS.txt --quiet) || { echo "Test releases changed since they were hashed." >&2; exit 1; }

SRC="$WORK/stager-src"; rm -rf "$SRC"; mkdir -p "$SRC"
git -C "$GITROOT" archive "$RC9_SOURCE:$SUBDIR" | tar -x -C "$SRC"
mkdir -p "$SRC/tools/updater-offline-stager"
cp "$REPO/tools/updater-offline-stager/"{Carowinds.OfflineUpdateStager.csproj,Program.cs} "$SRC/tools/updater-offline-stager/"
cp "$TESTREL/test-trust/appsettings-updater-test.json" "$SRC/tools/updater-offline-stager/test-trust.json"

PUB="$WORK/stager-publish"; rm -rf "$PUB"
dotnet publish "$SRC/tools/updater-offline-stager/Carowinds.OfflineUpdateStager.csproj" -c Release -r win-x64 --self-contained true \
  -p:Version=0.0.900-stager -p:AssemblyVersion=0.1.0.0 \
  -p:SourceRevisionId="$(git -C "$GITROOT" rev-parse "$RC9_SOURCE")-offline-stager" -o "$PUB/carowinds-offline-update-stager" > "$WORK/stager-publish.log"
find "$PUB" -name '*.pdb' -delete   # same rule as the product packages: no debug symbols
cat > "$PUB/carowinds-offline-update-stager/README-UPDATER-TEST.txt" <<'EOF'
UPDATER TEST ONLY — offline update stager

Test tool for checking the Carowinds Operations updater on a disposable Windows
test profile without GitHub. It verifies a signed test package (throwaway test key
only), unpacks it the way the app does after a download, writes the install
request and starts the updater that came inside the package. It is not
Carowinds Operations and must never be used with real Carowinds data.
See docs/releases/rc9-updater-test-harness.md §8.

Third-party components: .NET runtime and Microsoft.Data.Sqlite / Microsoft.Extensions
(MIT), SQLitePCLRaw (Apache-2.0), SQLite (public domain).
EOF

mkdir -p "$KIT/packages"
cp "$TESTREL/local-only/"*.zip "$KIT/"
cp -R "$TESTREL/upload-candidates/." "$KIT/packages/"
python3 -I - "$PUB" "carowinds-offline-update-stager" "$KIT/LOCAL-ONLY-offline-update-stager-win-x64.zip" <<'PY'
import os, sys, zipfile
root, name, zip_path = sys.argv[1:]
with zipfile.ZipFile(zip_path, "x", zipfile.ZIP_DEFLATED) as z:
    for directory, _, files in sorted(os.walk(os.path.join(root, name))):
        for f in sorted(files):
            full = os.path.join(directory, f)
            z.write(full, os.path.relpath(full, root).replace(os.sep, "/"))
PY
(cd "$KIT" && find . -type f ! -name SHA256SUMS.txt | sort | xargs shasum -a 256 > SHA256SUMS.txt)
chmod -R a-w "$OUT"
echo "Offline kit ready in $KIT (nothing uploaded)."
