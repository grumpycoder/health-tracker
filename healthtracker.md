Create an iOS app for personal fitness, recovery, nutrition, and health tracking.

App name: Fit Recovery Log

Purpose:
The app should help track daily workouts, exercise progression, meals, body measurements, recovery factors, and medication schedules. The goal is to identify trends over time and help decide when to increase workout difficulty or adjust recovery.

Core Features:

1. Daily Dashboard
- Show today’s date and planned status:
  - Workout day
  - Recovery day
  - Active recovery day
  - High physical workload day
- Show quick entry buttons for:
  - Start workout
  - Log meal/snack
  - Log body measurement
  - Log medication
  - Log sleep
  - Log soreness/fatigue
  - Add daily note

2. Workout Tracking
- Allow users to create reusable workout routines.
- A routine contains multiple exercises.
- Each exercise should support:
  - Exercise name
  - Target reps
  - Target sets
  - Duration option
  - Rest time
  - Equipment/setup notes
  - Progression notes

Example routine:
- Incline push-ups, 20 reps x 3 sets, incline height about 18 inches
- Lunges, 10-12 reps per leg x 3 sets
- March in place, duration or rep based
- Glute bridges, 12-15 reps x 3 sets
- Crunches, 15-20 reps x 3 sets

3. Workout Timer
- Include a start workout button.
- Track total workout duration from start to finish.
- Allow optional timers for rest periods.
- Allow marking each exercise/set as completed.
- Save total workout time automatically.
- Allow manual adjustment if needed.

4. Exercise Feedback
For each exercise after a workout, allow optional notes:
- Felt easy
- Moderate
- Hard
- Very hard
- Pain/discomfort
- Breathing difficulty
- Form issues
- Custom comment

Also allow exercise-specific notes like:
- “Rep 20 on final push-up set was very hard.”
- “Squats burned more due to sore thighs.”
- “Back tender before workout.”

5. Progression Tracking
The app should help identify when an exercise may be ready to progress.

Track:
- Completion consistency
- Difficulty rating over time
- Reps completed
- Workout time trend
- User comments
- Breathing/fatigue notes
- Soreness before and after workout

Progression suggestions:
- If exercise is rated easy for several workouts, suggest increasing difficulty.
- If final reps remain hard, suggest holding current level.
- If pain is reported, suggest caution or recovery.

6. Meal Logger
Allow quick meal/snack logging with:
- Time
- Meal type:
  - Breakfast
  - Lunch
  - Dinner
  - Snack
  - Drink
- Food description
- Portion note
- Optional tags:
  - High protein
  - High carb
  - High sodium
  - Sweet drink
  - Restaurant meal
  - Home-cooked
  - Recovery meal
- Optional satiety note:
  - Still hungry
  - Satisfied
  - Full
  - Bloated
  - Empty stomach feeling

Track recurring foods and drinks:
- Eggs
- Turkey
- Chicken
- Rice
- Mini-Wheats
- Protein bars
- Bananas
- Sweet tea
- Coke Zero
- Coffee

7. Nutrition Pattern Tracking
Allow the app to summarize trends:
- Sweet tea ounces per day
- Coffee cups per day
- Sugar cubes per coffee
- Protein bars per day
- Restaurant meals
- Late evening meals
- Meals linked to bloating
- Meals linked to strong hunger later

8. Body Measurement Logger
Track physical measurements:
- Body weight
- Waist measurement
- Optional chest, hips, arms, thighs
- Clothing fit notes:
  - Belt tighter/looser
  - Shorts fitting looser
  - Pants sagging
  - Shirt fit changes
- Progress photos optional

Measurement schedule:
- Weight: configurable, default weekly
- Waist: configurable, default weekly
- Photos: optional monthly reminder

9. Sleep and Recovery Logger
Track:
- Sleep duration
- Sleep score
- Number/quality of interruptions
- Notes about disrupted sleep
- Recovery rating
- Fatigue rating
- Soreness locations:
  - Lower back
  - Mid-back
  - Thighs
  - Shoulders
  - Core
  - Other
- Soreness severity:
  - None
  - Mild
  - Moderate
  - Severe
- Notes about physical workload such as yard work, dog care, lifting, house maintenance, or travel.

10. Physical Workload Logger
Track non-workout physical activity:
- Yard work
- Weed pulling
- Grass cutting
- House maintenance
- Car detailing
- Dog care/lifting
- Walking
- Travel/long driving
- Other

Each entry should allow:
- Duration
- Intensity:
  - Light
  - Moderate
  - Heavy
- Notes
- Body areas affected

11. Medication / Health Tracking
Include optional medication tracking:
- Medication name
- Dose
- Frequency
- Date/time taken
- Injection site if applicable
- Notes

Specific use case:
- TRT injection tracking
  - Dose
  - Injection date
  - Injection site
  - Any reaction or soreness
  - Lab timing relative to injection

Also track labs:
- Testosterone
- Free testosterone
- Hematocrit
- Hemoglobin
- PSA
- A1C
- Lipids
- Other custom lab values

Allow reminders for:
- TRT injection date
- Hematocrit lab check
- Body measurements
- Weekly progress review

12. Trend Reports
Create simple charts and summaries for:
- Weight over time
- Waist over time
- Workout duration over time
- Push-up reps over time
- Exercise difficulty over time
- Sleep score over time
- Sweet tea intake over time
- Soreness frequency
- Workout consistency
- Recovery days vs workout days

13. Weekly Review
Generate a weekly summary:
- Workouts completed
- Recovery days
- Average workout time
- Best performance note
- Weight/waist changes
- Nutrition observations
- Sleep/recovery observations
- Suggested focus for next week

14. Data Model Suggestions
Use SwiftData or Core Data.

Suggested entities:
- DailyLog
- WorkoutRoutine
- WorkoutSession
- ExerciseDefinition
- ExerciseSet
- ExerciseFeedback
- MealEntry
- DrinkEntry
- BodyMeasurement
- SleepEntry
- RecoveryEntry
- PhysicalWorkloadEntry
- MedicationEntry
- LabResult
- WeeklyReview

15. Design Requirements
- Clean, simple iOS interface
- Fast logging with minimal taps
- Dark mode support
- Calendar view
- Timeline view
- Charts view
- Search/filter by date, tag, exercise, food, or symptom
- Local-first storage
- iCloud sync optional
- Export to Markdown, CSV, or JSON

16. Primary User Goals
The app should help answer:
- Am I progressing?
- Should I increase workout difficulty?
- Am I recovering enough?
- Are certain foods causing bloating?
- Is my weight or waist trending down?
- Is sleep affecting workout performance?
- Are medications or injection timing affecting energy/performance?
- Am I eating enough on workout or high-activity days?

Build the app with a focus on practical daily logging and trend recognition rather than calorie obsession.