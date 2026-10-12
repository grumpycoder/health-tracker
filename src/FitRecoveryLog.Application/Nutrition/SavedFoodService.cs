using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Nutrition;

namespace FitRecoveryLog.Application.Nutrition;

public interface ISavedFoodRepository
{
    Task<SavedFood?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Case-insensitive, trimmed name match; the most recently updated row wins
    /// if two devices created the same name offline.</summary>
    Task<SavedFood?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<SavedFood>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(SavedFood food, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Use cases for the saved-foods library. Saves upsert by name so the library holds
/// one entry per food regardless of where the edit came from (library page or a meal item).</summary>
public sealed class SavedFoodService
{
    private readonly ISavedFoodRepository _repo;
    public SavedFoodService(ISavedFoodRepository repo) => _repo = repo;

    public Task<IReadOnlyList<SavedFood>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    /// <summary>Create or update the saved food named <paramref name="name"/> with per-serving macros.</summary>
    public async Task<Result<Guid>> SaveAsync(string name, Macros macros, string? servingNote = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result<Guid>.Failure("Food name is required.");
        var food = await _repo.FindByNameAsync(name, ct);
        if (food is null)
        {
            food = SavedFood.Create(name, macros, servingNote);
        }
        else
        {
            food.SetMacros(macros);
            food.SetServingNote(servingNote);
        }
        await _repo.SaveAsync(food, ct);
        return Result<Guid>.Success(food.Id);
    }

    /// <summary>Follow a library rename. If the target name already has a saved food it wins
    /// (the merge keeps the destination's macros) and the old entry is removed.</summary>
    public async Task<Result> RenameAsync(string oldName, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName)) return Result.Failure("Food name is required.");
        var food = await _repo.FindByNameAsync(oldName, ct);
        if (food is null) return Result.Success();   // nothing curated for this name
        var target = await _repo.FindByNameAsync(newName, ct);
        if (target is not null && target.Id != food.Id)
        {
            await _repo.RemoveAsync(food.Id, ct);
            return Result.Success();
        }
        food.Rename(newName);
        await _repo.SaveAsync(food, ct);
        return Result.Success();
    }

    /// <summary>Drop the curated macros so the food falls back to its most recent log.</summary>
    public async Task<Result> ResetAsync(string name, CancellationToken ct = default)
    {
        var food = await _repo.FindByNameAsync(name, ct);
        if (food is null) return Result.Failure("Food has no saved macros.");
        await _repo.RemoveAsync(food.Id, ct);
        return Result.Success();
    }
}
