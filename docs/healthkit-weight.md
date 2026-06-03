# Scope: HealthKit weight sync (write-on-save + manual pull)

## Goal

Mirror **body weight** with Apple Health:
- **Write-on-save** — when a weight is logged in the app, write it to Health automatically.
- **Manual pull** — a "Pull from Health" button imports weights logged elsewhere.

Two-way, but each direction is explicit (no live/background sync in this scope).

## Platform

- **iOS only** (iPhone/iPad + iOS simulator). **HealthKit is unavailable on Mac
  Catalyst**, and the HealthKit types aren't present for the `net9.0-maccatalyst`
  target — so the implementation must be compile-guarded with `#if IOS` (not just
  the runtime `OperatingSystem.IsIOS()` check used for reminders). The Mac build is
  a no-op.

## Capabilities / config (iOS)

- **Entitlement:** `com.apple.developer.healthkit` in `Platforms/iOS/Entitlements.plist`.
- **App ID capability:** enable HealthKit on the Apple Developer App ID (matters for
  device/signing; the simulator is lenient).
- **Info.plist usage strings:** `NSHealthShareUsageDescription` (read) and
  `NSHealthUpdateUsageDescription` (write).
- **Runtime authorization:** request read+write for the body-mass type on first use.

## Data mapping

- `BodyMeasurement.WeightLbs` ↔ `HKQuantityType` **bodyMass**, unit `HKUnit.Pound`,
  sample dated to the measurement's date.
- **Steps:** `HKQuantityType` **stepCount** — **read-only**, see below.
- (Phase 2) `WaistInches` ↔ `HKQuantityType` **waistCircumference** (`HKUnit.Inch`).

## Steps (read-only, not stored)

Decision: **do not capture an "end of day" step snapshot, and do not store steps in
the DB.** Read them from HealthKit on demand instead:

- HealthKit already retains full step history, so any day's total can be queried
  whenever needed — storing a daily copy adds dedup/staleness for no real gain.
- A true end-of-day capture would require background delivery
  (`HKObserverQuery` + background modes), which iOS throttles and is unreliable;
  reading mid-day also yields a *partial* total.

Implementation when built:
- `IHealthService.ReadStepsAsync(DateOnly date)` and/or
  `ReadDailyStepsAsync(DateOnly from, DateOnly to)` using `HKStatisticsQuery` /
  `HKStatisticsCollectionQuery` (cumulative sum, `HKUnit.Count`).
- Show today's steps live on the **dashboard**; build a **steps trend** by querying
  per day. Nothing persisted; Mac build simply shows nothing.

Revisit storing a `DailyLog.Steps` field (backfilled on app open, not end-of-day)
only if steps are later wanted in **export** or the **Mac build**.

## Architecture

- **`IHealthService`** (shared abstraction):
  - `bool IsAvailable`
  - `Task<bool> RequestAuthorizationAsync()`
  - `Task WriteWeightAsync(DateOnly date, double lbs, Guid sourceId)`
  - `Task<IReadOnlyList<(DateOnly Date, double Lbs)>> ReadWeightsAsync(DateTime since)`
- **iOS implementation** (`Platforms/iOS`, or a file behind `#if IOS`) using
  `HKHealthStore`: `RequestAuthorization` for bodyMass; `Save(HKQuantitySample)` to
  write; `HKSampleQuery` to read.
- **Non-iOS:** a no-op implementation with `IsAvailable = false`.
- Registered in `MauiProgram` (iOS resolves the real one; others the no-op) and
  injected into the Measurements page.

## Write-on-save

- In `Measurements.Save`, after the DB save, if `IsAvailable` + authorized and
  `WeightLbs` has a value, write a bodyMass sample dated to the entry's date.
- Stamp written samples with app metadata (e.g. `HKMetadataKeyExternalUuid` = the
  measurement `Id`) so a later pull can skip samples this app authored.

## Manual pull

- A **"Pull from Health"** button on the Measurements page.
- Read bodyMass samples since a cutoff (last import / last 90 days). For each day:
  - skip if a `BodyMeasurement` already exists for that date,
  - skip samples whose source is **this app** (avoid re-importing our own writes),
  - otherwise create a `BodyMeasurement { Date, WeightLbs }` (convert to pounds).
- One weight per date (use the latest sample that day).

## Edge cases / tradeoffs

- **Double counting:** write-then-pull could duplicate; mitigated by source/metadata
  filtering + per-date dedup. Keep pull conservative.
- **Units:** Health stores in the user's preferred unit; always convert via
  `HKUnit.Pound` so the app stays in lbs.
- **Permission denied / partial:** the app must work fully without HealthKit —
  writes/pulls just skip. iOS hides read-denial for privacy, so reads may simply
  return empty; handle gracefully (no error).
- **No live sync:** external Health edits won't reflect until the next manual pull
  (observers are out of scope).
- **Simulator vs device:** the simulator has HealthKit but sparse data; a real
  device (with signing + the HealthKit capability) is needed for meaningful testing.

## Effort & risk

- **Medium.** Mostly iOS-native HealthKit plumbing plus entitlement/Info.plist
  config. Low risk to the existing app (guarded; no-op elsewhere). Main fiddliness:
  entitlements/usage strings and dedup.

## Phasing

- **Phase 1:** `IHealthService` + iOS impl; request auth (bodyMass read+write,
  stepCount read); write-on-save for weight; "Pull from Health" for weight with
  per-date dedup; **read-only steps** on the dashboard + a steps trend.
- **Phase 2 (optional):** waist circumference; write workouts to Health; background
  observer (`HKObserverQuery`/anchored) for true live two-way sync; persist
  `DailyLog.Steps` if export/Mac coverage is wanted.

## Out of scope

- Heart rate / sleep import, workouts→Health, live observers, waist, and any
  stored/end-of-day step snapshot (deferred to phase 2).
