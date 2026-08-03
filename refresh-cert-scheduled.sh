#!/usr/bin/env bash
#
# Scheduled entry point for the weekly cert refresh — invoked by the launchd
# agent (com.mlawrence.fitlog.refresh). Sets a login-like PATH (launchd's is
# minimal), runs refresh-cert.sh, appends to a log, and posts a macOS
# notification with the result. Run manually with:  bash refresh-cert-scheduled.sh
#
export PATH="/opt/homebrew/bin:/usr/local/share/dotnet:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:$HOME/.dotnet/tools"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG="$HOME/Library/Logs/fitlog-refresh.log"
mkdir -p "$(dirname "$LOG")"

{
  echo ""
  echo "===== $(date '+%Y-%m-%d %H:%M:%S') weekly refresh ====="
  "$SCRIPT_DIR/refresh-cert.sh"
  rc=$?
  echo "exit code: $rc"
} >>"$LOG" 2>&1

# Notify only when it matters (routine daily heals stay silent):
#   10 = weekly renewal succeeded · 2 = urgent (cert expired, can't reach phone)
#    1 = build/sign error · 0,3 = routine/non-urgent → silent
case "${rc:-1}" in
  10) osascript -e 'display notification "Signing profile refreshed for the week ✓" with title "Fit Log"' >/dev/null 2>&1 ;;
  2)  osascript -e 'display notification "Cert expired and the iPhone is unreachable — unlock & connect it (it will retry), or run refresh-cert.sh." with title "Fit Log ⚠️" sound name "Basso"' >/dev/null 2>&1 ;;
  1)  osascript -e 'display notification "Cert refresh failed to build/sign — check Xcode → Settings → Accounts, then run refresh-cert.sh." with title "Fit Log ⚠️" sound name "Basso"' >/dev/null 2>&1 ;;
esac
exit "${rc:-1}"
