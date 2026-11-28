#!/usr/bin/env bash
# Pre-RC.15 pricing-retention verification harness — Windows x64 LOCAL-ONLY package (build host only; uploads nothing).
# docs/pre-rc15-post-closing-pricing-retention.md §11.
#
#   tools/pricing-retention-harness/build-harness.sh
#
# Publishes the harness self-contained for win-x64 from the committed HEAD (refuses a dirty tree) into
#   artifacts/local-test/RC15-PricingRetentionHarness-LOCALTEST/LOCAL-ONLY-RC15-PricingRetentionHarness-LOCALTEST-win-x64.zip
# The output folder is immutable: the script refuses if it already exists. No database, PDB or user data is packaged;
# the synthetic fixtures are embedded in the harness assembly.
set -euo pipefail

LABEL=RC15-PricingRetentionHarness-LOCALTEST
REPO=$(cd "$(dirname "$0")/../.." && pwd)
OUT="$REPO/artifacts/local-test/$LABEL"
NAME="LOCAL-ONLY-$LABEL-win-x64"

[[ -z "$(git -C "$REPO" status --porcelain)" ]] || { echo "Working tree is not clean; commit first." >&2; exit 1; }
[[ -e "$OUT" ]] && { echo "$OUT already exists; local-test packages are immutable. Move it aside first." >&2; exit 1; }
COMMIT=$(git -C "$REPO" rev-parse HEAD)

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
PUB="$WORK/$NAME"
dotnet publish "$REPO/tools/pricing-retention-harness/Carowinds.PricingRetentionHarness.csproj" -c Release -r win-x64 --self-contained true \
  -p:Version=0.1.0-rc.15-LOCALTEST.pricing-retention-harness -p:SourceRevisionId="$COMMIT" -o "$PUB" > "$WORK/publish.log"
find "$PUB" -name '*.pdb' -delete
if find "$PUB" -iname '*.db' -o -iname '*.db-wal' -o -iname '*.db-shm' -o -iname '*.sqlite' | grep -q .; then
  echo "A database file was found in the publish output; refusing to package it." >&2; exit 1
fi

cat > "$PUB/README-PRICING-RETENTION-TEST.txt" <<EOF
LOCAL-ONLY TEST TOOL - NOT A RELEASE - SYNTHETIC DATA ONLY

Carowinds pricing-retention verification harness ($LABEL)
Source commit: $COMMIT

Use only in the disposable Windows account "CarowindsTest", with the RC15-PricingRetention-LOCALTEST app.
It writes made-up Daily Admission and Fast Lane prices for Thursday, August 20, 2026 into that account's
Carowinds Passdown data, using the app's own pricing code with recorded synthetic answers. It makes no network
request and never changes the Windows clock. It refuses to run for any other Windows user, while the app is open,
or if August 20, 2026 already has stored prices.

  1. Open the test app once, then close it.
  2. In this folder: Shift+right-click > Open in Terminal, then run
       .\\carowinds-pricing-retention-harness.exe seed
  3. Follow docs/releases/rc15-pricing-retention-localtest-windows-checklist.md, section "Simulated closing".
  4. After restarting the app, run  .\\carowinds-pricing-retention-harness.exe verify

Delete this folder when testing is finished.
EOF

mkdir -p "$OUT"
(cd "$WORK" && zip -qrX "$OUT/$NAME.zip" "$NAME")
chmod a-w "$OUT/$NAME.zip"
(cd "$OUT" && shasum -a 256 "$NAME.zip" > SHA256SUMS.txt && chmod a-w SHA256SUMS.txt)
echo "ZIP:     $OUT/$NAME.zip"
echo "Bytes:   $(stat -f %z "$OUT/$NAME.zip" 2>/dev/null || stat -c %s "$OUT/$NAME.zip")"
echo "Files:   $(unzip -Z1 "$OUT/$NAME.zip" | grep -v '/$' | wc -l | tr -d ' ')"
echo "SHA-256: $(cut -d' ' -f1 "$OUT/SHA256SUMS.txt")"
echo "Source:  $COMMIT"
