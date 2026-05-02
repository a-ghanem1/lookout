using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Lookout.Core.Capture;

/// <summary>
/// Tracks <see cref="DbCommand"/> instances that have been claimed by the EF Core interceptor
/// so that the ADO.NET diagnostic subscriber can skip them.
/// De-dupe rule: EF wins — richer metadata (DbContext type, typed CommandType) is preserved.
/// </summary>
/// <remarks>
/// Uses a <see cref="ConditionalWeakTable{TKey,TValue}"/> so entries are GC'd with the command;
/// no memory leak risk. Thread-safe: <see cref="ConditionalWeakTable{TKey,TValue}"/> guarantees
/// concurrent read/write safety.
/// </remarks>
public static class EfCommandRegistry
{
    private static readonly ConditionalWeakTable<DbCommand, object> _efOwned = new();

    /// <summary>
    /// Set to <c>true</c> by <c>Lookout.EntityFrameworkCore</c>'s <c>AddEntityFrameworkCore()</c>
    /// so that provider-level subscribers (e.g. the Npgsql ActivitySource subscriber) can defer
    /// to the richer EF interceptor instead of double-capturing the same query.
    /// </summary>
    public static bool EfInterceptorRegistered { get; set; }

    /// <summary>
    /// Marks <paramref name="command"/> as EF-owned. Idempotent: safe to call multiple times
    /// for the same command instance.
    /// </summary>
    public static void Mark(DbCommand command) =>
        _efOwned.GetOrCreateValue(command);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="command"/> was previously marked by
    /// <see cref="Mark"/>.
    /// </summary>
    public static bool IsMarked(DbCommand command) =>
        _efOwned.TryGetValue(command, out _);
}
