using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

		builder.Services.AddMauiBlazorWebView();

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
#if DEBUG
			// Populate an empty database with sample data for development.
			DevSeed.SeedIfEmpty(db);
#endif
		}

		return app;
	}
}
