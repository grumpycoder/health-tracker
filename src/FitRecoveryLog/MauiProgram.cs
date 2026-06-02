using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FitRecoveryLog;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
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
#endif

		var app = builder.Build();

		// Create the database/schema on first run. (Swap to EF migrations once the
		// schema stabilizes; EnsureCreated does not handle incremental schema changes.)
		using (var scope = app.Services.CreateScope())
		{
			var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
			using var db = factory.CreateDbContext();
			db.Database.EnsureCreated();
		}

		return app;
	}
}
