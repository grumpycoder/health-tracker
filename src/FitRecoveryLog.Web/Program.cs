using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using FitRecoveryLog.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    // Request the sync API's scope by default so acquired tokens are valid for it.
    options.ProviderOptions.DefaultAccessTokenScopes.Add(SyncApi.Scope);
});

// HttpClient that auto-attaches the API token to calls bound for the cloud API.
builder.Services.AddScoped<ApiAuthorizationMessageHandler>();
builder.Services
    .AddHttpClient("api", client => client.BaseAddress = new Uri(SyncApi.BaseUrl))
    .AddHttpMessageHandler<ApiAuthorizationMessageHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));
builder.Services.AddScoped<WebSyncClient>();
builder.Services.AddScoped<AppState>();

// Clean Architecture: application use cases over API-backed repositories, so the web runs
// the same domain logic as the phone.
builder.Services.AddScoped<FitRecoveryLog.Application.Workouts.IRoutineRepository, FitRecoveryLog.Web.Infrastructure.ApiRoutineRepository>();
builder.Services.AddScoped<FitRecoveryLog.Application.Workouts.IWorkoutSessionRepository, FitRecoveryLog.Web.Infrastructure.ApiWorkoutSessionRepository>();
builder.Services.AddScoped<FitRecoveryLog.Application.Workouts.RoutineService>();

// Workouts: aggregate use cases + domain-event dispatch (WorkoutCompleted -> mark the day).
builder.Services.AddScoped<FitRecoveryLog.Application.Workouts.IWorkoutRepository, FitRecoveryLog.Web.Infrastructure.ApiWorkoutRepository>();
builder.Services.AddScoped<FitRecoveryLog.Application.Workouts.IDayTypeService, FitRecoveryLog.Web.Infrastructure.ApiDayTypeService>();
builder.Services.AddScoped<FitRecoveryLog.Application.Common.IDomainEventDispatcher, FitRecoveryLog.Application.Common.DomainEventDispatcher>();
builder.Services.AddScoped<FitRecoveryLog.Application.Common.IDomainEventHandler<FitRecoveryLog.Domain.Workouts.Events.WorkoutCompleted>, FitRecoveryLog.Application.Workouts.WorkoutCompletedHandler>();
builder.Services.AddScoped<FitRecoveryLog.Application.Workouts.WorkoutService>();

await builder.Build().RunAsync();
