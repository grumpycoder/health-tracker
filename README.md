# Fit Recovery Log

Personal iOS app for tracking workouts, recovery, nutrition, body measurements,
medication/labs, and trends. Built for personal use only — **not** intended for
the App Store.

See [`healthtracker.md`](./healthtracker.md) for the full feature spec.

## Stack

- **.NET 9 MAUI Blazor Hybrid** — UI in Razor/HTML/CSS, native iOS shell (C# throughout)
- **EF Core + SQLite** — local-first storage on-device (`fitrecoverylog.db3`)
- Targets: `net9.0-ios` (phone/simulator) and `net9.0-maccatalyst` (fast dev on the Mac)

## Requirements

- macOS + **Xcode 26.5** (the .NET iOS SDK band `26.5` requires it)
- .NET 9 SDK + MAUI workload (`sudo dotnet workload install maui`)

## Run

```bash
cd src/FitRecoveryLog

# Fast iteration on the Mac (no simulator needed):
dotnet build -f net9.0-maccatalyst
open bin/Debug/net9.0-maccatalyst/maccatalyst-arm64/FitRecoveryLog.app

# iOS Simulator (requires an installed iOS simulator runtime):
dotnet build -t:Run -f net9.0-ios
```

Easiest day-to-day: open `FitRecoveryLog.sln` in **Rider** and pick a run target.

## Data

- SQLite DB lives in the app's private data dir
  (`~/Library/Containers/com.mlawrence.fitrecoverylog/Data/Library/fitrecoverylog.db3`
  for MacCatalyst; sandboxed on device).
- Schema is created via `EnsureCreated()` on startup. **This does not handle
  incremental schema changes** — once the entity model stabilizes, switch to EF
  Core migrations. Until then, changing an entity means deleting the local DB.

## Status

Implemented:
- Daily Dashboard (day-type selector, daily note, quick-log buttons)
- Body Measurement logger + history
- Meal & Drink logger (tags, satiety, common-food autocomplete, today's timeline)
- Sleep & Recovery logger (sleep metrics, recovery/fatigue, soreness locations)
- Medication & Labs logger (incl. TRT injection sites; common labs)
- Full entity/data model for all spec features

Not yet built: workout tracking/timer, physical workload logger, trend charts,
weekly review, export, HealthKit.

## Notes

- **HealthKit** (Apple Watch/iPhone auto data) is reachable from .NET iOS but not
  yet wired in — v1 is manual entry.
- **iCloud sync** (spec lists as optional) is not implemented; it's an Apple-native
  feature that's awkward from MAUI. Entity keys are GUIDs to keep future sync open.
