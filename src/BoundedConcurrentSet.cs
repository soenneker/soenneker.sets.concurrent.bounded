using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Soenneker.Atomics.Longs;
using Soenneker.Sets.Concurrent.Bounded.Abstract;

namespace Soenneker.Sets.Concurrent.Bounded;

/// <summary>
/// A high-throughput, thread-safe set that attempts to stay under a maximum size.
/// Not strict under contention: may temporarily exceed <see cref="MaxSize"/> but will
/// opportunistically trim on writes to push back under the limit.
/// 
/// Eviction is best-effort FIFO-ish (insertion order), optimized for throughput rather than strict ordering.
/// Uses generation tokens to avoid unbounded stale scanning in the FIFO queue.
/// </summary>
public sealed class BoundedConcurrentSet<T> : IBoundedConcurrentSet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, Entry> _index;
    private readonly ConcurrentQueue<Node> _fifo;

    private readonly int _trimBatchSize;
    private readonly int _trimStartThreshold;
    private readonly int _maxTrimWorkPerCall;
    private readonly int _resyncAfterNoProgress;

    private readonly int _queueOverageTrimThreshold; // soft threshold to opportunistically prune stale nodes

    private AtomicLong _approxCount;
    private AtomicLong _nextGen;
    private AtomicLong _approxQueued; // approximate queue depth (enq - deq)

    // Not atomic: losing increments is fine; it's a heuristic trigger.
    private int _noProgressTrimStreak;

    public long ApproxCount => _approxCount.Read();

    public int MaxSize { get; }

    public IEnumerable<T> Values => _index.Keys;

    /// <summary>
    /// Creates a bounded concurrent set.
    /// </summary>
    /// <param name="maxSize">Target maximum size to stay under (best-effort).</param>
    /// <param name="capacityHint">Optional initial capacity hint for the dictionary.</param>
    /// <param name="trimBatchSize">How many candidates to attempt per trim cycle.</param>
    /// <param name="trimStartOveragePercent">
    /// How far above max size we allow before trimming starts (reduces thrash under contention).
    /// Example: 5 means start trimming at maxSize * 1.05.
    /// </param>
    /// <param name="maxTrimWorkPerCall">Hard cap on per-call trim work (upper bound on dequeues).</param>
    /// <param name="resyncAfterNoProgress">
    /// Resync ApproxCount from dictionary count after this many consecutive trim calls remove nothing.
    /// Set to 0 to disable resync.
    /// </param>
    /// <param name="queueOverageFactor">
    /// Soft cap multiplier for queued nodes vs MaxSize. When approx queue depth exceeds MaxSize * factor,
    /// trims will opportunistically prune additional stale nodes (bounded).
    /// </param>
    /// <param name="comparer">Optional key comparer.</param>
    public BoundedConcurrentSet(int maxSize, int capacityHint = 0, int trimBatchSize = 64, int trimStartOveragePercent = 5, int maxTrimWorkPerCall = 4096,
        int resyncAfterNoProgress = 8, int queueOverageFactor = 4, IEqualityComparer<T>? comparer = null)
    {
        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize));

        if (trimBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(trimBatchSize));

        if (trimStartOveragePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(trimStartOveragePercent));

        if (maxTrimWorkPerCall <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTrimWorkPerCall));

        if (resyncAfterNoProgress < 0)
            throw new ArgumentOutOfRangeException(nameof(resyncAfterNoProgress));

        if (queueOverageFactor < 1)
            throw new ArgumentOutOfRangeException(nameof(queueOverageFactor));

        MaxSize = maxSize;
        _trimBatchSize = trimBatchSize;
        _maxTrimWorkPerCall = maxTrimWorkPerCall;
        _resyncAfterNoProgress = resyncAfterNoProgress;

        long threshold = maxSize + (long)Math.Ceiling(maxSize * (trimStartOveragePercent / 100.0));
        _trimStartThreshold = threshold > int.MaxValue ? int.MaxValue : (int)threshold;

        long qThresh = (long)maxSize * queueOverageFactor;
        _queueOverageTrimThreshold = qThresh > int.MaxValue ? int.MaxValue : (int)qThresh;

        int concurrencyLevel = Math.Max(2, Environment.ProcessorCount);

        _index = capacityHint > 0
            ? new ConcurrentDictionary<T, Entry>(concurrencyLevel, capacityHint, comparer)
            : new ConcurrentDictionary<T, Entry>(concurrencyLevel, 31, comparer);

        _fifo = new ConcurrentQueue<Node>();

        _approxCount = new AtomicLong(0);
        _nextGen = new AtomicLong(0);
        _approxQueued = new AtomicLong(0);
    }

    public bool TryAdd(T value)
    {
        // Per-key generation prevents old queue nodes from matching a newer re-add.
        long gen = _nextGen.Increment();

        if (_index.TryAdd(value, new Entry(gen)))
        {
            _approxCount.Increment();

            _fifo.Enqueue(new Node(value, gen));
            _approxQueued.Increment();

            // Best-effort: only start trimming when we're meaningfully above max.
            if (_approxCount.Read() > _trimStartThreshold)
                TrimBestEffort();

            // Opportunistically prune stale backlog if queue is ballooning (bounded work inside).
            if (_approxQueued.Read() > _queueOverageTrimThreshold)
                PruneQueueBestEffort();

            return true;
        }

        // Even if it existed, help trim when heavily over.
        if (_approxCount.Read() > _trimStartThreshold)
            TrimBestEffort();

        // If queue ballooned, pruning can reduce future trim costs even if the set is stable.
        if (_approxQueued.Read() > _queueOverageTrimThreshold)
            PruneQueueBestEffort();

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T value) => _index.ContainsKey(value);

    public bool TryRemove(T value)
    {
        if (_index.TryRemove(value, out _))
        {
            _approxCount.Decrement();
            return true;
        }

        return false;
    }

    public T[] ToArray() => _index.Keys.ToArray();

    /// <summary>
    /// Evicts older candidates (best-effort) until under MaxSize or we exhaust the work budget.
    /// Uses generation tokens to avoid costly dictionary removes for stale queue entries.
    /// </summary>
    private void TrimBestEffort()
    {
        long count = _approxCount.Read();
        if (count <= MaxSize)
            return;

        long over = count - MaxSize;

        long budget = Math.Max(over, _trimBatchSize);
        if (budget > _maxTrimWorkPerCall)
            budget = _maxTrimWorkPerCall;

        long removed = 0;

        while (budget-- > 0)
        {
            // Keep the loop bounded, but don't hammer atomics every iteration if we're already likely under.
            // Quick local check every time we actually remove something.
            if (_approxCount.Read() <= MaxSize)
                break;

            if (!_fifo.TryDequeue(out Node node))
                break;

            _approxQueued.Decrement();

            // Fast stale check: only proceed if this queued generation is still the current live generation.
            if (!_index.TryGetValue(node.Value, out Entry entry) || entry.Gen != node.Gen)
                continue;

            // Remove only if the entry is exactly the one we observed (prevents removing a newer re-add).
            if (_index.TryRemove(new KeyValuePair<T, Entry>(node.Value, entry)))
            {
                _approxCount.Decrement();
                removed++;
            }
        }

        if (removed == 0)
        {
            int streak = ++_noProgressTrimStreak;

            if (_resyncAfterNoProgress != 0 && streak >= _resyncAfterNoProgress)
            {
                ResyncApproxCount();
                _noProgressTrimStreak = 0;
            }
        }
        else
        {
            _noProgressTrimStreak = 0;
        }
    }

    /// <summary>
    /// Opportunistically prunes stale queue nodes when the queue depth balloons (bounded work).
    /// Does NOT try to reduce set size; it just reduces future stale scanning overhead.
    /// </summary>
    private void PruneQueueBestEffort()
    {
        // Budget is intentionally smaller than TrimBestEffort; this is a hygiene pass.
        long budget = Math.Min(_trimBatchSize, 256);

        while (budget-- > 0)
        {
            if (_approxQueued.Read() <= _queueOverageTrimThreshold)
                break;

            if (!_fifo.TryDequeue(out Node node))
                break;

            _approxQueued.Decrement();

            // If it's stale, great — we removed trash.
            // If it might be live, we leave it alone (don’t remove from set here).
            // We still paid only a bounded dequeue cost.
        }
    }

    /// <summary>
    /// Low-frequency correction path: sync approx count with dictionary count.
    /// This is intentionally rare because ConcurrentDictionary.Count is not free.
    /// </summary>
    private void ResyncApproxCount()
    {
        int exact = _index.Count;
        long current = _approxCount.Read();

        // AtomicLong doesn't expose exchange here; adjust via delta.
        long delta = exact - current;

        if (delta > 0)
        {
            while (delta-- > 0)
                _approxCount.Increment();
        }
        else if (delta < 0)
        {
            while (delta++ < 0)
                _approxCount.Decrement();
        }
    }

    private readonly record struct Node(T Value, long Gen);

    private readonly record struct Entry(long Gen);
}