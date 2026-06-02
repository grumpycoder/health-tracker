using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FitRecoveryLog.Data;

/// <summary>
/// Used only by the `dotnet ef` tooling at design time to construct an
/// <see cref="AppDbContext"/> without running the MAUI app. The connection
/// string here is irrelevant to migrations (they read the model, not data),
/// but the SQLite provider must be configured so the right migrations are
/// generated.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new AppDbContext(options);
    }
}
