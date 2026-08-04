#!/usr/bin/env bash
#
# refresh-cert.sh — keep Fit Log's free 7-day signing profile current and the app
# installed. Safe to run often (e.g. daily): it only re-mints the profile when it's
# within RENEW_WINDOW_DAYS of expiry, and always (re)installs so a phone that was
# absent during an earlier run gets caught up. Pass --force to re-mint regardless
# (e.g. to reset the week early).
#
# Exit codes (used by the launchd wrapper to decide whether to notify):
#   10 = minted a fresh profile AND installed (the weekly renewal)
#    0 = installed, no mint needed (routine heal) — silent
#    3 = install failed but profile still valid >1 day (will retry later) — silent
#    2 = install failed and profile expired/≤1 day (URGENT — connect the phone)
#    1 = hard error (build/sign failed, e.g. Xcode Apple-ID session lapsed)
#
set -uo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Personal values (device UDID, stub-project path) live in a gitignored config so
# they stay out of the repo. Copy refresh-cert.config.example → refresh-cert.config.
[ -f "$REPO_DIR/refresh-cert.config" ] && source "$REPO_DIR/refresh-cert.config"
: "${DEVICE_ID:?Set DEVICE_ID in refresh-cert.config (see refresh-cert.config.example)}"
: "${XCODE_PROJECT:?Set XCODE_PROJECT in refresh-cert.config (see refresh-cert.config.example)}"
BUNDLE_ID="${BUNDLE_ID:-com.mlawrence.fitrecoverylog}"
XCODE_SCHEME="${XCODE_SCHEME:-fitrecoverylog}"
APP_DIR="$REPO_DIR/src/FitRecoveryLog"
APP_PATH="$APP_DIR/bin/Debug/net9.0-ios/ios-arm64/FitRecoveryLog.app"
PROFILE_DIRS=(
  "$HOME/Library/MobileDevice/Provisioning Profiles"
  "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"
)
RENEW_WINDOW_DAYS=2
FORCE=0; [ "${1:-}" = "--force" ] && FORCE=1

appid_of()  { security cms -D -i "$1" 2>/dev/null | plutil -extract Entitlements.application-identifier raw - 2>/dev/null || true; }
expiry_of() { security cms -D -i "$1" 2>/dev/null | plutil -extract ExpirationDate raw - 2>/dev/null || true; }

# Newest expiry (epoch seconds) across profiles for our bundle; 0 if none.
newest_epoch=0
for dir in "${PROFILE_DIRS[@]}"; do
  [ -d "$dir" ] || continue
  while IFS= read -r p; do
    [ -n "$p" ] || continue
    [[ "$(appid_of "$p")" == *".$BUNDLE_ID" ]] || continue
    exp="$(expiry_of "$p")"; [ -n "$exp" ] || continue
    e=$(date -j -u -f "%Y-%m-%dT%H:%M:%SZ" "$exp" "+%s" 2>/dev/null || echo 0)
    [ "$e" -gt "$newest_epoch" ] && newest_epoch=$e
  done < <(find "$dir" -name '*.mobileprovision' 2>/dev/null)
done
now=$(date +%s)
pre_mint_epoch=$newest_epoch   # remember pre-mint state for urgency at failure

MINTED=0
if [ "$FORCE" = 1 ] || [ "$newest_epoch" -le "$(( now + RENEW_WINDOW_DAYS*86400 ))" ]; then
  echo "==> Profile missing or within $RENEW_WINDOW_DAYS days of expiry — minting fresh…"
  for dir in "${PROFILE_DIRS[@]}"; do
    [ -d "$dir" ] || continue
    while IFS= read -r p; do
      [ -n "$p" ] || continue
      [[ "$(appid_of "$p")" == *".$BUNDLE_ID" ]] && { echo "    removing $(basename "$p")"; rm -f "$p"; }
    done < <(find "$dir" -name '*.mobileprovision' 2>/dev/null)
  done
  if ! xcodebuild -allowProvisioningUpdates -project "$XCODE_PROJECT" -scheme "$XCODE_SCHEME" \
          -destination 'generic/platform=iOS' build >/tmp/refresh-cert-xcodebuild.log 2>&1; then
    echo "!! xcodebuild failed — usually the Apple ID session expired (Xcode → Settings → Accounts)."
    tail -8 /tmp/refresh-cert-xcodebuild.log
    exit 1
  fi
  MINTED=1
else
  echo "==> Profile still valid ($(date -r "$newest_epoch" '+%Y-%m-%d %H:%M')) — skipping mint."
fi

echo "==> Clean rebuild of the app…"
rm -rf "$APP_DIR/bin/Debug/net9.0-ios" "$APP_DIR/obj/Debug/net9.0-ios"
if ! ( cd "$APP_DIR" && dotnet build -f net9.0-ios -p:RuntimeIdentifier=ios-arm64 ) ; then
  echo "!! Build failed — see output above."
  exit 1
fi

echo "==> Installing on device…"
installed=0
for i in 1 2 3 4 5 6; do
  out="$(xcrun devicectl device install app --device "$DEVICE_ID" "$APP_PATH" 2>&1 || true)"
  if grep -q "launchServicesIdentifier" <<<"$out"; then installed=1; break; fi
  echo "    attempt $i failed ($(grep -ioE 'error [0-9]+|connection reset|locked|expired' <<<"$out" | head -1)); retrying…"
  sleep 5
done

if [ "$installed" -ne 1 ]; then
  # Urgent only if the app on the phone is (about to be) dead: profile expired/≤1 day.
  if [ "$pre_mint_epoch" -le "$(( now + 86400 ))" ]; then
    echo "!! Install failed and the profile is expired/expiring — connect & unlock the iPhone."
    exit 2
  fi
  echo "!! Install failed but the profile is still valid — will retry on the next run."
  exit 3
fi

xcrun devicectl device process launch --device "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 \
  && echo "    launched ✓" || echo "    (couldn't auto-launch — open Fit Log manually to confirm)"
if [ "$MINTED" = 1 ]; then echo "==> Done — profile refreshed for the week."; exit 10; fi
echo "==> Done — app up to date (no mint needed)."; exit 0
