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

if [ "${rc:-1}" -eq 0 ]; then
  osascript -e 'display notification "Signing profile refreshed for the week ✓" with title "Fit Log"' >/dev/null 2>&1
else
  osascript -e 'display notification "Refresh failed — unlock & connect the iPhone, then run refresh-cert.sh. Log: ~/Library/Logs/fitlog-refresh.log" with title "Fit Log ⚠️" sound name "Basso"' >/dev/null 2>&1
fi
exit "${rc:-1}"
