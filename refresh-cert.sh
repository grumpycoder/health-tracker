#!/usr/bin/env bash
#
# refresh-cert.sh — reset Fit Log's free 7-day signing profile and redeploy.
# Run it weekly (e.g. Mondays). No Rider/Xcode GUI needed, as long as the Apple
# ID (your-apple-id@example.com) is still signed into Xcode → Settings → Accounts.
#
# What it does:
#   1. Deletes the current com.mlawrence.fitrecoverylog profile (forces a fresh mint)
#   2. Mints a new 7-day profile via `xcodebuild -allowProvisioningUpdates` against
#      the matching stub Xcode project
#   3. Clean-rebuilds the MAUI app so it embeds the fresh profile
#   4. Installs to the iPhone (retries transient connection drops) and launches it
#
set -uo pipefail

# ---- config -----------------------------------------------------------------
BUNDLE_ID="com.mlawrence.fitrecoverylog"
DEVICE_ID="<YOUR_DEVICE_UDID>"                 # Mark's iPhone 13 Pro Max
XCODE_PROJECT="/Users/YOURNAME/source/ios_temp/fitrecoverylog/fitrecoverylog.xcodeproj"
XCODE_SCHEME="fitrecoverylog"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$REPO_DIR/src/FitRecoveryLog"
APP_PATH="$APP_DIR/bin/Debug/net9.0-ios/ios-arm64/FitRecoveryLog.app"
PROFILE_DIRS=(
  "$HOME/Library/MobileDevice/Provisioning Profiles"
  "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"
)

appid_of() { security cms -D -i "$1" 2>/dev/null | plutil -extract Entitlements.application-identifier raw - 2>/dev/null || true; }
expiry_of() { security cms -D -i "$1" 2>/dev/null | plutil -extract ExpirationDate raw - 2>/dev/null || true; }

# ---- 1. delete the current profile so a fresh one gets minted ---------------
echo "==> Removing existing $BUNDLE_ID profiles (forces a fresh 7-day mint)…"
for dir in "${PROFILE_DIRS[@]}"; do
  [ -d "$dir" ] || continue
  while IFS= read -r p; do
    [ -n "$p" ] || continue
    if [[ "$(appid_of "$p")" == *".$BUNDLE_ID" ]]; then
      echo "    removing $(basename "$p")"
      rm -f "$p"
    fi
  done < <(find "$dir" -name '*.mobileprovision' 2>/dev/null)
done

# ---- 2. mint a fresh profile ------------------------------------------------
echo "==> Minting a fresh profile via xcodebuild -allowProvisioningUpdates…"
if ! xcodebuild -allowProvisioningUpdates -project "$XCODE_PROJECT" -scheme "$XCODE_SCHEME" \
        -destination 'generic/platform=iOS' build >/tmp/refresh-cert-xcodebuild.log 2>&1; then
  echo "!! xcodebuild failed — usually the Apple ID session expired."
  echo "   Fix: Xcode → Settings (⌘,) → Accounts → sign in to your-apple-id@example.com, then re-run."
  tail -8 /tmp/refresh-cert-xcodebuild.log
  exit 1
fi

echo "==> New profile(s) for $BUNDLE_ID:"
for dir in "${PROFILE_DIRS[@]}"; do
  [ -d "$dir" ] || continue
  while IFS= read -r p; do
    [ -n "$p" ] || continue
    [[ "$(appid_of "$p")" == *".$BUNDLE_ID" ]] && echo "    expires $(expiry_of "$p")  ($(basename "$p"))"
  done < <(find "$dir" -name '*.mobileprovision' 2>/dev/null)
done

# ---- 3. clean rebuild so the app embeds the fresh profile -------------------
echo "==> Clean rebuild of the app…"
rm -rf "$APP_DIR/bin/Debug/net9.0-ios" "$APP_DIR/obj/Debug/net9.0-ios"
if ! ( cd "$APP_DIR" && dotnet build -f net9.0-ios -p:RuntimeIdentifier=ios-arm64 ) ; then
  echo "!! Build failed — see output above."
  exit 1
fi

# ---- 4. install (retry transient drops) + launch ---------------------------
echo "==> Installing on device…"
installed=0
for i in 1 2 3 4 5 6; do
  out="$(xcrun devicectl device install app --device "$DEVICE_ID" "$APP_PATH" 2>&1 || true)"
  if grep -q "launchServicesIdentifier" <<<"$out"; then installed=1; break; fi
  echo "    attempt $i failed ($(grep -ioE 'error [0-9]+|connection reset|locked|expired' <<<"$out" | head -1)); retrying…"
  sleep 5
done

if [ "$installed" -ne 1 ]; then
  echo "!! Install failed after retries. Unlock the iPhone, keep it on Wi-Fi, accept 'Trust This Computer', then re-run."
  exit 1
fi

echo "==> Installed. Launching to confirm the signature is accepted…"
if xcrun devicectl device process launch --device "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1; then
  echo "    launched ✓"
else
  echo "    (couldn't auto-launch — unlock the phone and open Fit Log manually to confirm)"
fi
echo "==> Done. Signing profile refreshed for the week."
