using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
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
			// Tapping a notification opens the route it carries (default: Reminders).
			LocalNotificationCenter.Current.NotificationActionTapped += e =>
			{
				if (e.IsTapped)
					NotificationNav.Go(string.IsNullOrWhiteSpace(e.Request?.ReturningData) ? "reminders" : e.Request.ReturningData);
			};
		}
		builder.Services.AddMauiBlazorWebView();

		// HealthKit (iOS only); no-op everywhere else (e.g. Mac Catalyst).
#if IOS
		builder.Services.AddSingleton<FitRecoveryLog.Services.IHealthService, HealthKitService>();
#else
		builder.Services.AddSingleton<FitRecoveryLog.Services.IHealthService, FitRecoveryLog.Services.NoopHealthService>();
#endif

		// Holds an in-progress workout so it survives navigating away from the page.
		builder.Services.AddSingleton<FitRecoveryLog.Services.ActiveWorkoutState>();
		// App-wide transient toast.
		builder.Services.AddSingleton<FitRecoveryLog.Services.ToastService>();
		// Local automatic daily backup snapshots.
		builder.Services.AddSingleton<FitRecoveryLog.Services.AutoBackup>();

		// Cloud sync (MSAL sign-in + push/pull against the Azure sync API).
		builder.Services.AddSingleton<FitRecoveryLog.Services.IAccessTokenProvider, FitRecoveryLog.Services.MsalAuthService>();
		builder.Services.AddSingleton<FitRecoveryLog.Services.CloudSyncService>();

		// Clean Architecture: application use cases over real (EF) repositories.
		builder.Services.AddSingleton<FitRecoveryLog.Application.Workouts.IRoutineRepository, FitRecoveryLog.Infrastructure.Workouts.EfRoutineRepository>();
		builder.Services.AddSingleton<FitRecoveryLog.Application.Workouts.IWorkoutSessionRepository, FitRecoveryLog.Infrastructure.Workouts.EfWorkoutSessionRepository>();
		builder.Services.AddSingleton<FitRecoveryLog.Application.Workouts.RoutineService>();

#if IOS || MACCATALYST
		// Silent sync when the app returns to the foreground (in addition to the launch
		// sync). No-op if not signed in; the service's IsSyncing guard prevents overlap.
		builder.ConfigureLifecycleEvents(events =>
			events.AddiOS(ios => ios.WillEnterForeground(app =>
			{
				var sync = IPlatformApplication.Current?.Services?.GetService<FitRecoveryLog.Services.CloudSyncService>();
				if (sync is not null)
					_ = Task.Run(() => sync.SyncAsync(allowInteractive: false));
			})));
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

		// Best-effort silent sync on startup — a no-op if the user isn't signed in
		// (never pops UI here; interactive sign-in happens from the Settings page).
		_ = Task.Run(async () =>
		{
			try { await app.Services.GetRequiredService<FitRecoveryLog.Services.CloudSyncService>().SyncAsync(allowInteractive: false); }
			catch { /* surfaced on manual sync in Settings */ }
		});

		return app;
	}
}
