# Fit Log — Weekly Device Refresh Runbook

Fit Log is signed with a **free Apple Developer account**, so its provisioning
profile **expires every 7 days**. When it lapses the app shows a grayed-out
**"unavailable"** icon and won't open. This runbook re-signs and reinstalls it.

**Cadence:** roughly once a week, or any time the app icon goes gray / won't
launch. It's safe to run anytime — it does **not** touch the app's data.

**Environment (fixed):** this Mac, Apple ID `your-apple-id@example.com`,
your iPhone. Do not sign in with a different Apple ID — that
changes the signing identity and would reinstall the app as a new one
(wiping its data).

Key facts you'll need:
- **Repo:** `/path/to/health-tracker`
- **App project dir:** `src/FitRecoveryLog` (always build from here)
- **Device ID:** `<YOUR_DEVICE_UDID>`
- **Bundle ID:** `com.mlawrence.fitrecoverylog`

---

## Weekly reset — one command (preferred)

Run this once a week (e.g. Monday) with the iPhone unlocked and on Wi‑Fi:

```bash
/path/to/health-tracker/refresh-cert.sh
```

It deletes the current profile, mints a **fresh 7-day one** from the terminal
(via `xcodebuild -allowProvisioningUpdates` against the stub Xcode project at
`/path/to/stub/fitrecoverylog`), rebuilds, installs, and
launches to confirm. **No Rider or Xcode GUI needed** — the *only* thing that
still needs the GUI is keeping the Apple ID signed into **Xcode → Settings →
Accounts** (that session lapses every so often; if the script reports an
`xcodebuild` failure, re-sign in there and re-run).

Notes:
- You won't get a "Trust" prompt on the phone — trust is tied to the signing
  *certificate*, which doesn't change; only the profile is renewed.
- The stub Xcode project must keep the same bundle id
  (`com.mlawrence.fitrecoverylog`), team `<YOUR_TEAM_ID>`, automatic signing, and
  the **HealthKit** capability, or the minted profile won't match the app.

If the script fails partway, fall back to the manual steps below.

### Automatic (launchd)

A launchd agent runs the refresh **daily at 9:00 AM and 7:00 PM** (or the next
wake if the Mac was asleep). It's self-healing: each run only **re-mints** the
profile when it's within ~2 days of expiry, and **always tries to install**, so
if the iPhone was disconnected at one run it gets caught up at the next one the
phone is present. Notifications are quiet by design — you only get one on the
weekly renewal (✓) or when something needs you (cert expired and phone
unreachable, or a build/sign error). Routine daily heals are silent. Pieces:
- `refresh-cert-scheduled.sh` — the entry point (sets PATH, logs, notifies)
- `tools/com.mlawrence.fitlog.refresh.plist` → installed at
  `~/Library/LaunchAgents/com.mlawrence.fitlog.refresh.plist`
- Log: `~/Library/Logs/fitlog-refresh.log`

Manage it:
```bash
launchctl list | grep fitlog                                   # is it loaded?
launchctl start com.mlawrence.fitlog.refresh                   # run now
launchctl unload ~/Library/LaunchAgents/com.mlawrence.fitlog.refresh.plist   # disable
launchctl load -w ~/Library/LaunchAgents/com.mlawrence.fitlog.refresh.plist  # re-enable
```

---

## Before you start (every time)

1. **Unlock the iPhone and keep it awake** — plug it in via USB. Most failures
   are just a locked/asleep phone.
2. Make sure the phone is **on Wi‑Fi / has internet** (iOS verifies the app
   online on first launch after install).
3. If macOS asks **"Trust this computer?"** on the phone, tap **Trust**.

---

## Happy path (try this first)

Open Terminal and run:

```bash
cd /path/to/health-tracker/src/FitRecoveryLog
dotnet build -f net9.0-ios -p:RuntimeIdentifier=ios-arm64
```

Confirm the output shows **`Build succeeded`** and a line like
`Provisioning Profile: "... com.mlawrence.fitrecoverylog" (…) - 7 entitlements`.

Then install (this retry loop handles the common transient
"Connection interrupted" / device-busy errors):

```bash
for i in 1 2 3 4 5; do
  out=$(xcrun devicectl device install app --device <YOUR_DEVICE_UDID> \
    bin/Debug/net9.0-ios/ios-arm64/FitRecoveryLog.app 2>&1)
  echo "$out" | grep -q "launchServicesIdentifier" && { echo "INSTALLED"; break; }
  echo "attempt $i failed, retrying…"; sleep 5
done
```

When you see **`INSTALLED`**, open the app on the phone. Done.

> If the app was already expired, after install you may get a one-time trust
> prompt — see **Trust the app** below.

---

## If the build FAILS or install is rejected

The profile has fully expired or gone missing. Work down this ladder, checking
after each step whether the happy path now works.

### Step 1 — Re-mint via Rider (fastest)
1. Open the solution in **JetBrains Rider**.
2. Select the **iPhone** as the run target and click **Run** ▶.
3. Rider silently mints a fresh 7‑day profile and installs. A cosmetic
   `MT0000` / `FormatException` at the very end (after signing succeeds) is a
   known harmless bug — ignore it.
4. Retry the **Happy path** commands above.

### Step 2 — Re-authenticate the Apple ID (if Rider didn't help)
1. **Xcode → Settings (⌘,) → Accounts.**
2. Select `your-apple-id@example.com`. If there's a ⚠️ or a "sign in again"
   prompt, complete it (re-enter the Apple ID password).
3. Re-run **Step 1**.

### Step 3 — Force a fresh profile with a throwaway Xcode project
Use this if the profile directory is empty or install fails with
`0xe8008015 "A valid provisioning profile … was not found"`.

1. **Xcode → File → New → Project → iOS App.**
2. Set **Bundle Identifier to exactly** `com.mlawrence.fitrecoverylog`
   — *recovery + log*, easy to mistype (don't drop the **y**). Team = the
   personal team, **Automatic** signing.
3. **Important:** select the target → **Signing & Capabilities** → **+ Capability**
   → add **HealthKit**. (Without this the minted profile lacks HealthKit and the
   install is rejected.)
4. Select the **iPhone** as the destination and **Build** (⌘B). Let it provision.
5. Delete the throwaway project.
6. Back in Terminal, force a clean rebuild so the new profile gets embedded, then
   reinstall:

```bash
cd /path/to/health-tracker/src/FitRecoveryLog
rm -rf bin/Debug/net9.0-ios obj/Debug/net9.0-ios
dotnet build -f net9.0-ios -p:RuntimeIdentifier=ios-arm64
```

Confirm the build says **`7 entitlements`** and shows **no `MT7140`** warning,
then run the install retry loop from the Happy path.

To sanity-check the profile on disk (both locations are valid):

```bash
for D in "$HOME/Library/MobileDevice/Provisioning Profiles" \
         "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"; do
  find "$D" -name '*.mobileprovision' 2>/dev/null | while read -r p; do
    echo "== $(basename "$p") =="
    security cms -D -i "$p" | plutil -extract Entitlements xml1 -o - - \
      | grep -iE "application-identifier|healthkit" | sed 's/^ *//'
  done
done
```

A good profile shows an `application-identifier` ending in **`.fitrecoverylog`**
(with the **y**) and three `healthkit` keys.

---

## Trust the app (only after a fresh reinstall, if it won't open)

1. On the phone, make sure **Developer Mode** is on:
   **Settings → Privacy & Security → Developer Mode** → on → reboot if prompted.
2. **Tap the Fit Log icon once** (this creates the trust entry — it won't appear
   until you try to open the app).
3. **Settings → General → VPN & Device Management → Developer App →
   `Apple Development: your-apple-id@example.com` → Trust** → confirm.
4. Open the app.

If **Developer Mode keeps needing a toggle** or the Trust row won't appear:
toggle Developer Mode **off → reboot → on → reboot**. This clears iOS's stale
trust cache (it's a symptom of the weekly expiry, not a real setting flip).

---

## Troubleshooting quick reference

| Symptom | Cause | Fix |
|---|---|---|
| `CoreDeviceError 1011` / "unable to locate device" | Phone locked, asleep, or off Wi‑Fi | Unlock + keep awake, then retry |
| `Connection interrupted` / error 3002 / 4000 | Transient | Just retry (the loop does this) |
| App icon grayed / **"unavailable"** | Profile expired or app not trusted | Reinstall (ladder above) + **Trust the app** |
| `0xe8008011 provisioning profile has expired` | 7‑day profile lapsed | Ladder **Step 1**, then 2, then 3 |
| `0xe8008015 valid provisioning profile not found` | Profile missing / lacks a needed entitlement | Ladder **Step 3** (throwaway project **with HealthKit**) |
| Build error `NETSDK1005` / wrong target framework | Built from repo root (resolves the .sln) | Always `cd src/FitRecoveryLog` first |
| "device is locked" / "developer disk image could not be mounted" | Phone locked | Unlock the phone |

---

## Permanent fix (stops the weekly chore)

Enrolling in the **Apple Developer Program ($99/yr)** replaces the 7‑day profile
with a ~1‑year one — no more weekly re-signing or trust dance — and unlocks
TestFlight (over‑the‑air installs, no cable or trust step). That's the real
long‑term answer if this refresh becomes a burden.
