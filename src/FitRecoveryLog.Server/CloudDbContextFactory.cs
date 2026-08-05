using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FitRecoveryLog.Server;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the SQL Server model and scaffold
/// migrations without running the Functions host. The connection string is irrelevant
/// to <c>migrations add</c> (it reads the model, not a live DB); it only matters for
/// <c>database update</c>. Migrations live in this project (SQL Server dialect), separate
/// from the phone's SQLite migrations in FitRecoveryLog.Data.
/// </summary>
public sealed class CloudDbContextFactory : IDesignTimeDbContextFactory<CloudDbContext>
{
    public CloudDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("SqlConnectionString")
                 ?? "Server=(localdb)\\mssqllocaldb;Database=fitrecoverylog-design;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<CloudDbContext>()
            .UseSqlServer(cs, sql => sql.MigrationsAssembly(typeof(CloudDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new CloudDbContext(options);
    }
}
