using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace FitRecoveryLog;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Write any unhandled exception to a file we can inspect during development.
		var crashLog = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
		void Log(string source, Exception? ex) =>
			File.AppendAllText(crashLog, $"[{DateTime.Now:HH:mm:ss}] {source}: {ex}\n\n");

		AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) => Log("TaskScheduler", e.Exception);
		File.AppendAllText(crashLog, $"[{DateTime.Now:HH:mm:ss}] ---- app started, logging active ({crashLog}) ----\n");

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// Local notifications are iOS-only here (the plugin has no MacCatalyst impl).
		if (ReminderNotifier.IsSupported)
		{
			builder.UseLocalNotification();
			// Tapping a notification opens the Reminders page.
			LocalNotificationCenter.Current.NotificationActionTapped += e =>
			{
				if (e.IsTapped) NotificationNav.Go("reminders");
			};
		}
		builder.Services.AddMauiBlazorWebView();

		// HealthKit (iOS only); no-op everywhere else (e.g. Mac Catalyst).
#if IOS
		builder.Services.AddSingleton<FitRecoveryLog.Services.IHealthService, HealthKitService>();
#else
		builder.Services.AddSingleton<FitRecoveryLog.Services.IHealthService, FitRecoveryLog.Services.NoopHealthService>();
#endif

		// Local-first SQLite database stored in the app's private data directory.
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "fitrecoverylog.db3");
		builder.Services.AddDbContextFactory<AppDbContext>(options =>
			options.UseSqlite($"Data Source={dbPath}"));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
		builder.Logging.AddProvider(new FileLoggerProvider(crashLog));
#endif

		var app = builder.Build();

		// Apply pending EF Core migrations on startup: creates the schema on first
		// run and evolves it on later runs without wiping data.
		using (var scope = app.Services.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
			using var db = factory.CreateDbContext();
			db.Database.Migrate();

			// One-time wipe + reseed from real history (health-history.json).
			// The marker file keeps it from re-running on every launch.
			// v4: current-week (Jun 1-3) events appended.
			var importMarker = Path.Combine(FileSystem.AppDataDirectory, "history-import-v4.done");
			if (!File.Exists(importMarker))
			{
				try
				{
					HistorySeed.ApplyEmbedded(db);
					File.WriteAllText(importMarker, DateTime.Now.ToString("O"));
				}
				catch (Exception ex) { Log("HistorySeed", ex); }
			}
			// Legacy: per-day DailyLog notes move into timestamped NoteEntries
			// (idempotent — converted notes are cleared from the DailyLog).
			var legacyNotes = db.DailyLogs.Where(d => d.Note != null && d.Note != "").ToList();
			if (legacyNotes.Count > 0)
			{
				foreach (var d in legacyNotes)
				{
					db.NoteEntries.Add(new NoteEntry { Time = d.Date.ToDateTime(new TimeOnly(12, 0)), Text = d.Note! });
					d.Note = null;
				}
				db.SaveChanges();
			}
#if DEBUG
			// Populate an empty database with sample data for development.
			DevSeed.SeedIfEmpty(db);
#endif
		}

		return app;
	}
}
