# Scope: Shared Exercise Library

## Problem

Today, each exercise is created **per routine**. `Routines.razor` `AddExercise`
news up a fresh `ExerciseDefinition` every time you add an exercise to a routine —
even if "Incline push-ups" already exists in another routine.

Consequences:
- **History/progression don't accumulate across routines.** `ExerciseSet` and
  `ExerciseFeedback` key off `ExerciseDefinitionId`, so the *same-named* exercise
  in two routines is two different identities with two separate histories.
- **Rebuilding a routine loses continuity.** A new "Incline push-ups" starts at
  zero history; the old one (with all the data) is what Progression keeps showing
  (until the recency filter ages it out).
- **Editing targets is entangled with identity.** Targets (sets/reps/duration/
  rest) live on `ExerciseDefinition`, so they can't differ per routine and editing
  them in one place is conceptually editing "the exercise," not "this routine's
  prescription."

## Goal

A single canonical list of exercises (a **library**). Routines *reference* library
exercises and carry their own per-routine prescription. History and progression
key off the library exercise, so data accumulates no matter which routine you used.

## Data model changes

Current (simplified):

```
ExerciseDefinition { Id, Name, Measure, TargetSets, TargetReps,
                     TargetDurationSeconds, RestSeconds, EquipmentNotes, ProgressionNotes }
RoutineExercise    { Id, RoutineId, ExerciseDefinitionId, Order }
ExerciseSet        { ..., ExerciseDefinitionId }   // already keys off the definition
ExerciseFeedback   { ..., ExerciseDefinitionId }   // already keys off the definition
```

Proposed:

```
ExerciseDefinition  // = the library entry; identity is Name (+ Measure)
  { Id, Name (unique, normalized), Measure, EquipmentNotes, ProgressionNotes }
  // NOTE: target fields REMOVED from here

RoutineExercise     // = this routine's prescription of a library exercise
  { Id, RoutineId, ExerciseDefinitionId, Order,
    TargetSets, TargetReps, TargetDurationSeconds, RestSeconds }   // MOVED here
```

`ExerciseSet` / `ExerciseFeedback` are **unchanged** — they already reference
`ExerciseDefinitionId`, so once definitions are shared, history aggregates
correctly for free. This is the key leverage: most of the payoff comes from
*not duplicating* definitions, not from restructuring history.

## Behavior changes

1. **Add to routine = pick-or-create.** Autocomplete against existing library
   exercises (match on normalized name). Reuse the existing `ExerciseDefinition`
   if found; only create a new one for a genuinely new name. Then set the
   per-routine targets on `RoutineExercise`.
2. **Editing a routine exercise** edits `RoutineExercise` targets (and optionally
   the library entry's name/notes), not a private copy.
3. **Removing an exercise from a routine** deletes only the `RoutineExercise`
   (unlink), **never** the `ExerciseDefinition` — preserving history. (This also
   resolves the open "soft-delete" item.) Library cleanup is a separate, explicit
   action.
4. **Workout start** reads targets from `RoutineExercise` instead of
   `ExerciseDefinition` (`Workout.razor` `StartWorkout`).

## UI changes

- **Routine add/edit** (`Routines.razor`): exercise name becomes a pick-from-library
  autocomplete + "create new"; targets bind to the `RoutineExercise` draft.
- **Workout / History / Progression**: no UI change needed — they already group by
  `ExerciseDefinitionId`; they just get more accurate.
- **(Phase 2, optional) Exercise library screen**: list/rename/merge/retire library
  exercises; show per-exercise lifetime history.

## Migration

Schema migration + a data migration:
- Add target columns to `RoutineExercise`; for each existing row, copy the targets
  from its current `ExerciseDefinition`.
- Drop target columns from `ExerciseDefinition`.
- **Dedupe** existing same-named definitions into one library entry and repoint
  `RoutineExercise`, `ExerciseSet`, `ExerciseFeedback` FKs to the survivor. This
  is the fiddly part for *existing* data.

Because the app is still in development (DEBUG seeder, disposable data), the
pragmatic path is: ship the new schema + a fresh migration and **wipe dev DBs**
rather than write the dedupe/repoint data migration. Revisit a real data migration
only once there's data worth preserving on a device.

## Effort & risk

- **Medium.** Touches the data model (migration), routine add/edit UI, workout
  start, delete semantics, and the seeder.
- Low *runtime* risk to history/progression (they already key off the definition).
- Main risk is the **dedupe data migration**; avoidable in dev by wiping.

## Suggested phasing

- **Phase 1** (core) — ✅ DONE: targets moved to `RoutineExercise`; pick-or-create
  library exercise on add (case-insensitive, unique name); remove = unlink only
  (keeps the library exercise + history); workout start + seeder updated; EF
  migration `SharedExerciseLibrary`. Dev DBs wiped rather than data-migrated.
- **Phase 2** (polish) — ✅ DONE: `/exercises` library screen with rename (global,
  unique-guarded), retire/restore (hidden from pickers, history kept), merge into
  another exercise (repoints sets/feedback/routine slots), and per-exercise lifetime
  history (stats + best-per-session chart + difficulty). `Retired` flag + migration.

## Related items this unblocks/absorbs

- **Soft-delete for exercises** — handled by unlink-only delete in Phase 1.
- **Progression accuracy** — suggestions follow an exercise across routine changes.
