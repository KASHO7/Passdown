#!/usr/bin/env bash
# Phase 27 updater test releases (docs/releases/rc9-updater-test-harness.md). Build-host only; uploads nothing.
#
#   build-updater-test-releases.sh <key-dir outside the repository> <work-dir outside the repository> <owner/test-repo> <pwsh>
#
# Produces, under artifacts/updater-test/ (git-ignored):
#   upload-candidates/v0.0.900-rc.N/  ZIP + .update.json + .update.json.sig for N = 2..6 (synthetic test app + the
#                                     exact frozen RC.9 updater bytes; signed with a throwaway test key)
#   local-only/                       the starting app: RC.8 source (a651e26) built with ONLY version and update
#                                     settings changed (test repository, test key). Contains the real app — never upload.
#   test-trust/                       the test public key as an appsettings fragment (no private material)
#   SHA256SUMS.txt
# Versions 0.0.900-rc.N sort below every real candidate, so no real installation could treat them as an update, and
# the production builds trust neither the test key nor the test repository.
set -euo pipefail

KEYDIR=$(cd "$1" && pwd); WORK=$2; TEST_REPO=$3; PWSH=$4
REPO=$(cd "$(dirname "$0")/../.." && pwd)
GITROOT=$(git -C "$REPO" rev-parse --show-toplevel)
SUBDIR=${REPO#"$GITROOT"/}
OUT="$REPO/artifacts/updater-test"
FROZEN_RC9="$REPO/artifacts/releases/Carowinds-Operations-0.1.0-rc.9-win-x64.zip"
FROZEN_RC9_SHA=e1ad521fe711dcee0c169cbd7244ea04f6d547df801c2f72b375bfd781fe1558
RC8_SOURCE=a651e26
RC9_SOURCE=e20d846
KEY_ID=updater-test-only-1
BASE=0.0.900

case "$KEYDIR" in "$GITROOT"*) echo "Key directory must be outside the repository." >&2; exit 1;; esac
mkdir -p "$WORK"; WORK=$(cd "$WORK" && pwd)
case "$WORK" in "$GITROOT"*) echo "Work directory must be outside the repository." >&2; exit 1;; esac
[[ "$TEST_REPO" =~ ^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$ && "$TEST_REPO" != */Passdown ]] || { echo "Refusing repository '$TEST_REPO'." >&2; exit 1; }
[[ -e "$OUT" ]] && { echo "$OUT already exists; test releases are immutable. Move it aside first." >&2; exit 1; }
[[ $(shasum -a 256 "$FROZEN_RC9" | cut -d' ' -f1) == "$FROZEN_RC9_SHA" ]] || { echo "Frozen RC.9 ZIP hash mismatch." >&2; exit 1; }

sign_tool() { dotnet run --project "$REPO/tools/update-signing" -c Release -- "$@"; }

# ---- keys (throwaway; private halves never enter the repository, the work folder or a package) ----
new_key() { # name -> prints public key
  local pem="$KEYDIR/$1.pem"
  [[ -e "$pem" ]] || sign_tool generate-key --private-key-out "$pem" > "$KEYDIR/$1.public.txt"
  tail -1 "$KEYDIR/$1.public.txt"
}
TEST_PUBLIC=$(new_key updater-test-only-1)
IMPOSTOR_PUBLIC=$(new_key updater-test-impostor)
[[ -n "$TEST_PUBLIC" && -n "$IMPOSTOR_PUBLIC" && "$TEST_PUBLIC" != "$IMPOSTOR_PUBLIC" ]]
mkdir -p "$OUT/test-trust" "$OUT/local-only" "$OUT/upload-candidates"
python3 -I - "$OUT/test-trust/appsettings-updater-test.json" "$TEST_REPO" "$KEY_ID" "$TEST_PUBLIC" <<'PY'
import json, sys
path, repo, key_id, public = sys.argv[1:]
json.dump({"Updates": {"Repository": repo, "Channel": "ReleaseCandidate", "TrustedSigningKeys": [{"KeyId": key_id, "PublicKey": public}]}}, open(path, "w"), indent=2)
PY

# ---- starting app: RC.8 source, only version + update settings changed (LOCAL ONLY) ----
FROM="$WORK/from-rc8-src"; rm -rf "$FROM"; mkdir -p "$FROM"
git -C "$GITROOT" archive "$RC8_SOURCE:$SUBDIR" | tar -x -C "$FROM"
sed -i '' "s#<Version>0.1.0-rc.8</Version>#<Version>$BASE-rc.1</Version>#; s#<FileVersion>0.1.0.8</FileVersion>#<FileVersion>$BASE.1</FileVersion>#" "$FROM/Directory.Build.props"
grep -q "<Version>$BASE-rc.1</Version>" "$FROM/Directory.Build.props"
python3 -I - "$FROM/src/Carowinds.Passdown.Desktop/appsettings.json" "$OUT/test-trust/appsettings-updater-test.json" <<'PY'
import json, sys
path, trust = sys.argv[1:]
settings = json.load(open(path)); settings["Updates"] = json.load(open(trust))["Updates"]
json.dump(settings, open(path, "w"), indent=2)
PY
(cd "$FROM" && SourceRevisionId=$(git -C "$GITROOT" rev-parse "$RC8_SOURCE")-updater-test "$PWSH" -NoProfile -File packaging/windows/Build-PortableRelease.ps1)
cp "$FROM/artifacts/releases/Carowinds-Operations-$BASE-rc.1-win-x64.zip" "$OUT/local-only/LOCAL-ONLY-updater-test-start-app-Carowinds-Operations-$BASE-rc.1-win-x64.zip"

# ---- new-version packages: synthetic test app + exact frozen RC.9 updater bytes ----
TO="$WORK/to-rc9-src"; rm -rf "$TO"; mkdir -p "$TO"
git -C "$GITROOT" archive "$RC9_SOURCE:$SUBDIR" | tar -x -C "$TO"
mkdir -p "$TO/tools/updater-test-harness"
cp "$REPO/tools/updater-test-harness/"{Carowinds.UpdaterTestHarness.csproj,Program.cs,test-release-notes.md} "$TO/tools/updater-test-harness/"
RC9X="$WORK/frozen-rc9"; rm -rf "$RC9X"; mkdir -p "$RC9X"; unzip -q "$FROZEN_RC9" -d "$RC9X"
RC9R="$RC9X/Carowinds-Operations-0.1.0-rc.9-win-x64"

build_package() { # N mode signer scenario
  local n=$1 mode=$2 signer=$3 scenario=$4 version="$BASE-rc.$1"
  local name="Carowinds-Operations-$version-win-x64" pub="$WORK/harness-$n" pkg="$WORK/pkg-$n"
  local dest="$OUT/upload-candidates/v$version"
  rm -rf "$pub" "$pkg"; mkdir -p "$pkg/$name/licenses" "$dest"
  dotnet publish "$TO/tools/updater-test-harness/Carowinds.UpdaterTestHarness.csproj" -c Release -r win-x64 --self-contained true \
    -p:Version="$version" -p:FileVersion="$BASE.$n" -p:AssemblyVersion=0.1.0.0 -p:HarnessMode="$mode" \
    -p:SourceRevisionId="$(git -C "$GITROOT" rev-parse "$RC9_SOURCE")-updater-test" -o "$pub" > "$WORK/publish-$n.log"
  python3 -I - "$RC9R" "$pub" "$pkg/$name" <<'PY'
import json, os, shutil, sys
rc9, harness, out = sys.argv[1:]
def listed(deps_path):
    deps = json.load(open(deps_path))
    target = next(v for k, v in deps["targets"].items() if k.endswith("/win-x64"))
    return {os.path.basename(p) for lib in target.values() for kind in ("runtime", "native") for p in (lib.get(kind) or {})}
updater = listed(os.path.join(rc9, "Carowinds.Passdown.Updater.deps.json"))
updater |= {f"Carowinds.Passdown.Updater.{x}" for x in ("exe", "dll", "deps.json", "runtimeconfig.json")}
for name in sorted(updater):                       # exact frozen RC.9 bytes for the updater and everything it loads
    shutil.copy2(os.path.join(rc9, name), os.path.join(out, name))
for name in ("exe", "dll", "deps.json", "runtimeconfig.json"):   # the synthetic app under the required name
    shutil.copy2(os.path.join(harness, f"Carowinds.Passdown.Desktop.{name}"), os.path.join(out, f"Carowinds.Passdown.Desktop.{name}"))
missing = sorted(listed(os.path.join(out, "Carowinds.Passdown.Desktop.deps.json")) - set(os.listdir(out)))
if missing: sys.exit(f"test app needs files the package lacks: {missing}")
for lic in ("dotnet-LICENSE.txt", "dotnet-THIRD-PARTY-NOTICES.txt", "Apache-2.0.txt"):
    shutil.copy2(os.path.join(rc9, "licenses", lic), os.path.join(out, "licenses", lic))
PY
  cat > "$pkg/$name/README-UPDATER-TEST.txt" <<EOF
UPDATER TEST ONLY — synthetic package $version ($scenario)

This is NOT Carowinds Operations. It exists only to test the Carowinds Operations
updater on a disposable Windows test profile. It contains:
  - a small synthetic test program named Carowinds.Passdown.Desktop.exe (the name
    the updater requires) that upgrades the local test database and reports what
    happened;
  - the unchanged Carowinds Operations 0.1.0-rc.9 updater and the components it
    needs (database access and the .NET runtime).
It contains no park data, no user data, no images, no PDF engine and no user
interface. It is signed with a throwaway test key that real installations do not
trust. Never run it on a computer or Windows account with real Carowinds data.

Third-party components: .NET runtime and Microsoft.Data.Sqlite / Microsoft.Extensions
(MIT, licenses\dotnet-LICENSE.txt and dotnet-THIRD-PARTY-NOTICES.txt), SQLitePCLRaw
(Apache-2.0, licenses\Apache-2.0.txt), SQLite (public domain).
EOF
  python3 -I - "$pkg" "$name" "$dest/$name.zip" <<'PY'
import os, sys, zipfile
root, name, zip_path = sys.argv[1:]
with zipfile.ZipFile(zip_path, "x", zipfile.ZIP_DEFLATED) as z:
    for directory, _, files in sorted(os.walk(os.path.join(root, name))):
        for f in sorted(files):
            full = os.path.join(directory, f)
            z.write(full, os.path.relpath(full, root).replace(os.sep, "/"))
PY
  cat > "$WORK/notes-$n.md" <<EOF
## UPDATER TEST ONLY

- Synthetic test package for the Carowinds Operations updater: $scenario.
- Not Carowinds Operations. Use only on a disposable Windows test profile.

## Before you install

- RC.9 updates the local database. A backup is required before installation. Older versions cannot open the updated database.
- The updater makes this backup for you and checks it before it changes any file. If the backup can't be made or checked, nothing is installed.
EOF
  sign_tool create-manifest --zip "$dest/$name.zip" --version "$version" --channel release-candidate \
    --title "UPDATER TEST ONLY — $scenario" --notes "$WORK/notes-$n.md" > /dev/null
  sign_tool sign --manifest "$dest/$name.update.json" --key-id "$KEY_ID" --private-key "$KEYDIR/$signer.pem" > /dev/null
}

build_package 2 Normal               updater-test-impostor "signature from an untrusted key (must be rejected)"
build_package 3 Normal               updater-test-only-1   "package altered after signing (download must be rejected)"
build_package 4 CrashBeforeMigration updater-test-only-1   "new version fails before the database upgrade (must roll back)"
build_package 5 CrashAfterMigration  updater-test-only-1   "new version fails after the database upgrade (must not roll back)"
build_package 6 Normal               updater-test-only-1   "successful update with database upgrade and What's New"

# rc.3: change one byte inside the signed ZIP (same size) so only the SHA-256 check can catch it.
python3 -I - "$OUT/upload-candidates/v$BASE-rc.3/Carowinds-Operations-$BASE-rc.3-win-x64.zip" <<'PY'
import sys
path = sys.argv[1]
data = bytearray(open(path, "rb").read()); i = len(data) // 2; data[i] ^= 0xFF
open(path, "wb").write(bytes(data))
PY

(cd "$OUT" && find . -type f ! -name SHA256SUMS.txt | sort | xargs shasum -a 256 > SHA256SUMS.txt)
chmod -R a-w "$OUT"
echo "Test releases ready in $OUT (nothing uploaded)."
