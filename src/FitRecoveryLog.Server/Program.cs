using FitRecoveryLog.Server;
using FitRecoveryLog.Server.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        // CORS first so browser preflight is answered before auth runs.
        worker.UseMiddleware<CorsMiddleware>();
        worker.UseMiddleware<AuthMiddleware>();
    })
    .ConfigureServices(services =>
    {
        var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString app setting is not configured.");

        // Serverless SQL auto-pauses when idle; retry rides out the resume instead of 500ing.
        services.AddDbContext<CloudDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null)));
        services.AddSingleton<TokenValidator>();
    })
    .Build();

host.Run();
