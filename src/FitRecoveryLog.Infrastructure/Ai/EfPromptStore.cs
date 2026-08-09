using FitRecoveryLog.Application.Ai;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Phone <see cref="IPromptStore"/>: reads an edited template from the local DB (synced from the
/// web). Returns the embedded <paramref name="fallback"/> when there's no row, the row is blank,
/// or the read fails — so the coach always has a usable prompt.
/// </summary>
public sealed class EfPromptStore : IPromptStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public EfPromptStore(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<string> GetTemplateAsync(string key, string fallback, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var row = await db.PromptTemplates.FirstOrDefaultAsync(p => p.PromptKey == key && !p.IsDeleted, ct);
            return string.IsNullOrWhiteSpace(row?.Text) ? fallback : row!.Text;
        }
        catch { return fallback; }
    }
}
