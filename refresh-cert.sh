#!/usr/bin/env bash
#
# refresh-cert.sh — keep Fit Log's free 7-day signing profile current and the app
# installed. Safe to run often (e.g. daily): it only re-mints the profile when it's
# within RENEW_WINDOW_DAYS of expiry, and always (re)installs so a phone that was
# absent during an earlier run gets caught up. Pass --force to re-mint regardless
# (e.g. to reset the week early).
#
# It builds the last COMMITTED code (HEAD), in a separate git worktree, never the
# working tree, so half-finished edits can't reach the phone. It also refuses to
# ship a build whose server migrations aren't applied to Azure SQL yet (a phone
# ahead of the server loses data on sync); then it reinstalls the last build it
# shipped instead, keeping the signing profile fresh.
#
# Exit codes (used by the launchd wrapper to decide whether to notify):
#   10 = minted a fresh profile AND installed (the weekly renewal)
#    0 = installed, no mint needed (routine heal) — silent
#    3 = install failed but profile still valid >1 day (will retry later) — silent
#    2 = install failed and profile expired/≤1 day (URGENT — connect the phone)
#    4 = Xcode Apple-ID session lapsed — re-sign in (Xcode → Settings → Accounts)
#    5 = HEAD needs a server migration that isn't applied — installed the previous build
#    1 = hard error (build/sign failed)
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
# Clean checkout of the commit being shipped (a git worktree, reused between runs).
BUILD_DIR="${BUILD_DIR:-$HOME/Library/Caches/fitlog-build}"
APP_DIR="$BUILD_DIR/src/FitRecoveryLog"
APP_PATH="$APP_DIR/bin/Release/net10.0-ios/ios-arm64/FitRecoveryLog.app"
# Last commit (and its newest server migration) installed on the phone.
STATE_FILE="$HOME/Library/Application Support/fitlog-refresh/installed"
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
  # Move (not delete) the old profiles so they can be restored if the mint fails —
  # otherwise a lapsed Apple-ID session leaves no profile at all.
  BACKUP_DIR="$(mktemp -d)"
  for dir in "${PROFILE_DIRS[@]}"; do
    [ -d "$dir" ] || continue
    while IFS= read -r p; do
      [ -n "$p" ] || continue
      [[ "$(appid_of "$p")" == *".$BUNDLE_ID" ]] || continue
      echo "    removing $(basename "$p")"
      mkdir -p "$BACKUP_DIR/$(basename "$dir")" && mv "$p" "$BACKUP_DIR/$(basename "$dir")/"
    done < <(find "$dir" -name '*.mobileprovision' 2>/dev/null)
  done
  if ! xcodebuild -allowProvisioningUpdates -project "$XCODE_PROJECT" -scheme "$XCODE_SCHEME" \
          -destination 'generic/platform=iOS' build >/tmp/refresh-cert-xcodebuild.log 2>&1; then
    for dir in "${PROFILE_DIRS[@]}"; do
      src="$BACKUP_DIR/$(basename "$dir")"
      [ -d "$src" ] && mv "$src"/*.mobileprovision "$dir/" 2>/dev/null
    done
    echo "    restored previous profile(s)"
    tail -8 /tmp/refresh-cert-xcodebuild.log
    if grep -qiE "No Accounts|session has expired|sign in again|Unable to log in|not signed in" /tmp/refresh-cert-xcodebuild.log; then
      echo "!! Xcode is signed out of the Apple ID — Xcode → Settings → Accounts, sign in, then re-run."
      [ "$pre_mint_epoch" -gt 0 ] && echo "   App on the phone expires $(date -r "$pre_mint_epoch" '+%a %b %d %H:%M')"
      exit 4
    fi
    echo "!! xcodebuild failed — see log above."
    exit 1
  fi
  rm -rf "$BACKUP_DIR"
  MINTED=1
else
  echo "==> Profile still valid ($(date -r "$newest_epoch" '+%Y-%m-%d %H:%M')) — skipping mint."
fi

# ---- Pick the commit to ship -------------------------------------------------
newest_migration() {   # newest server migration id in a checkout, e.g. 20261013135826_DrinkItems
  ls "$1/src/FitRecoveryLog.Server/Migrations" 2>/dev/null \
    | grep -E '^[0-9]{14}_.*\.cs$' | grep -v '\.Designer\.cs$' | sed 's/\.cs$//' | sort | tail -1
}
applied_migration() {  # newest migration applied to Azure SQL; empty if unreachable
  local params="$REPO_DIR/infra/main.parameters.json" pw
  [ -f "$params" ] && command -v sqlcmd >/dev/null || return 0
  pw="$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['parameters']['sqlAdminPassword']['value'])" "$params" 2>/dev/null)" || return 0
  for _ in 1 2 3; do   # the serverless DB may be paused — the first login wakes it
    out="$(SQLCMDPASSWORD="$pw" sqlcmd -S tcp:fitlog-sql-b6c3yia2b5u3g.database.windows.net -d fitrecoverylog \
          -U fitlogadmin -l 60 -h -1 -W -b \
          -Q "SET NOCOUNT ON; SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC" 2>/dev/null)" \
      && { echo "$out" | tr -d '[:space:]'; return 0; }
    sleep 20
  done
}
checkout() {           # point the build worktree at a commit
  git -C "$REPO_DIR" worktree prune
  if [ ! -e "$BUILD_DIR/.git" ]; then
    rm -rf "$BUILD_DIR"
    git -C "$REPO_DIR" worktree add -q --detach "$BUILD_DIR" "$1" || return 1
  fi
  git -C "$BUILD_DIR" checkout -q --detach --force "$1" && git -C "$BUILD_DIR" clean -qfd
}

HEAD_COMMIT="$(git -C "$REPO_DIR" rev-parse HEAD)"
LAST_COMMIT=""; LAST_MIGRATION=""
[ -f "$STATE_FILE" ] && read -r LAST_COMMIT LAST_MIGRATION < "$STATE_FILE"

dirty="$(git -C "$REPO_DIR" status --porcelain -- src)"
[ -n "$dirty" ] && { echo "==> Uncommitted changes NOT shipped (commit them to include):"; echo "$dirty" | sed 's/^/    /'; }

echo "==> Checking out $(git -C "$REPO_DIR" log -1 --format='%h %s' "$HEAD_COMMIT")…"
checkout "$HEAD_COMMIT" || { echo "!! Couldn't check out HEAD into $BUILD_DIR."; exit 1; }
SHIP_COMMIT="$HEAD_COMMIT"
need="$(newest_migration "$BUILD_DIR")"
have="$(applied_migration)"
HELD_BACK=0
if [ -n "$have" ] && [[ "$need" > "$have" ]]; then
  reason="server migration $need isn't applied to Azure SQL (newest there: $have)"
elif [ -z "$have" ] && [ -n "$LAST_COMMIT" ] && [ "$need" != "$LAST_MIGRATION" ]; then
  reason="couldn't reach Azure SQL to confirm migration $need is applied"
else
  reason=""
fi
if [ -n "$reason" ]; then
  echo "!! Not shipping HEAD — $reason."
  if [ -n "$LAST_COMMIT" ] && [ "$LAST_COMMIT" != "$HEAD_COMMIT" ]; then
    echo "   Reinstalling the last shipped build ($(git -C "$REPO_DIR" log -1 --format='%h %s' "$LAST_COMMIT")) instead."
    checkout "$LAST_COMMIT" || { echo "!! Couldn't check out $LAST_COMMIT."; exit 1; }
    SHIP_COMMIT="$LAST_COMMIT"; need="$LAST_MIGRATION"; HELD_BACK=1
  else
    echo "   No earlier shipped build recorded — run the migration (dotnet ef database update), then re-run."
    exit 5
  fi
fi

echo "==> Clean rebuild of the app…"
# Release build: ~40% faster launch than Debug. The interpreter stays on via
# MtouchInterpreter=all in the csproj (plain Release/AOT crashes this Blazor Hybrid app).
rm -rf "$APP_DIR/bin/Release/net10.0-ios" "$APP_DIR/obj/Release/net10.0-ios"
if ! ( cd "$APP_DIR" && dotnet build -f net10.0-ios -p:RuntimeIdentifier=ios-arm64 -c Release ) ; then
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

mkdir -p "$(dirname "$STATE_FILE")" && echo "$SHIP_COMMIT $need" > "$STATE_FILE"

xcrun devicectl device process launch --device "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 \
  && echo "    launched ✓" || echo "    (couldn't auto-launch — open Fit Log manually to confirm)"
if [ "$HELD_BACK" = 1 ]; then echo "==> Done — installed the previous build; HEAD is waiting on a server migration."; exit 5; fi
if [ "$MINTED" = 1 ]; then echo "==> Done — profile refreshed for the week."; exit 10; fi
echo "==> Done — app up to date (no mint needed)."; exit 0
