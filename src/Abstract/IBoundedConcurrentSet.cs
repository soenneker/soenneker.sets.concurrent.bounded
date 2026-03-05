using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace Soenneker.Sets.Concurrent.Bounded.Abstract;

/// <summary>
/// Represents a high-throughput, thread-safe set that attempts to stay under a configured maximum size.
/// </summary>
/// <typeparam name="T">The type of elements stored in the set.</typeparam>
/// <remarks>
/// Implementations prioritize throughput and low contention over strict size enforcement.
/// The set may temporarily exceed <see cref="MaxSize"/> under concurrency but will
/// opportunistically evict older entries to move back toward the limit.
/// 
/// Eviction ordering is typically best-effort FIFO based on insertion time,
/// but strict ordering guarantees are not provided.
/// </remarks>
public interface IBoundedConcurrentSet<T> where T : notnull
{
    /// <summary>
    /// Gets the approximate number of elements currently stored in the set.
    /// </summary>
    /// <remarks>
    /// This value is maintained using atomic operations and may be slightly inaccurate under heavy concurrency.
    /// It is intended for monitoring and heuristics rather than strict correctness.
    /// </remarks>
    [Pure]
    long ApproxCount { get; }

    /// <summary>
    /// Gets the configured maximum size the set attempts to stay under.
    /// </summary>
    [Pure]
    int MaxSize { get; }

    /// <summary>
    /// Gets a thread-safe enumerable view of the values currently contained in the set.
    /// </summary>
    /// <remarks>
    /// The returned enumerable reflects the underlying concurrent collection and
    /// may change while being enumerated. For a stable snapshot use <see cref="ToArray"/>.
    /// </remarks>
    [Pure]
    IEnumerable<T> Values { get; }

    /// <summary>
    /// Attempts to add a value to the set.
    /// </summary>
    /// <param name="value">The value to add.</param>
    /// <returns>
    /// <see langword="true"/> if the value was added;
    /// <see langword="false"/> if the value already existed in the set.
    /// </returns>
    /// <remarks>
    /// If the set grows beyond <see cref="MaxSize"/>, the implementation may opportunistically
    /// evict older entries to reduce the size.
    /// </remarks>
    bool TryAdd(T value);

    /// <summary>
    /// Determines whether the specified value exists in the set.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value exists; otherwise <see langword="false"/>.</returns>
    [Pure]
    bool Contains(T value);

    /// <summary>
    /// Attempts to remove a value from the set.
    /// </summary>
    /// <param name="value">The value to remove.</param>
    /// <returns>
    /// <see langword="true"/> if the value was removed; otherwise <see langword="false"/>.
    /// </returns>
    bool TryRemove(T value);

    /// <summary>
    /// Returns a snapshot array of the values currently in the set.
    /// </summary>
    /// <returns>An array containing the set's current values.</returns>
    [Pure]
    T[] ToArray();
}