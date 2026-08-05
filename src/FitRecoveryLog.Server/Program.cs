using FitRecoveryLog.Server;
using FitRecoveryLog.Server.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        worker.UseMiddleware<AuthMiddleware>();
    })
    .ConfigureServices(services =>
    {
        var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString app setting is not configured.");

        services.AddDbContext<CloudDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<TokenValidator>();
    })
    .Build();

host.Run();
