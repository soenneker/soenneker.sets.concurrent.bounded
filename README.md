[![](https://img.shields.io/nuget/v/soenneker.sets.concurrent.bounded.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.sets.concurrent.bounded/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.sets.concurrent.bounded/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.sets.concurrent.bounded/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.sets.concurrent.bounded.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.sets.concurrent.bounded/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.sets.concurrent.bounded/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.sets.concurrent.bounded/actions/workflows/codeql.yml)

# Soenneker.Sets.Concurrent.Bounded

A concurrent set with bounded-work, best-effort FIFO eviction after its configured size threshold is exceeded.

## Installation

```bash
dotnet add package Soenneker.Sets.Concurrent.Bounded
```

## Usage

```csharp
using Soenneker.Sets.Concurrent.Bounded;

var seenRequestIds = new BoundedConcurrentSet<string>(
    maxSize: 10_000,
    comparer: StringComparer.Ordinal);

if (!seenRequestIds.TryAdd(requestId))
{
    // The value was already present at the time of the add attempt.
    return;
}

bool isPresent = seenRequestIds.Contains(requestId);
bool removed = seenRequestIds.TryRemove(requestId);
string[] snapshot = seenRequestIds.ToArray();
```

`TryAdd` returns `false` for an existing value. A successful addition can still be evicted later by another add. `Contains`, `TryRemove`, and enumeration are thread-safe, but a result is only a point-in-time observation while other threads mutate the set.

## Bounding and eviction

This is not a strict-capacity collection. With the default five-percent overage, trimming begins only after the approximate count exceeds `maxSize + ceil(maxSize * 0.05)`. Concurrent additions and the per-call work budget can produce a larger temporary overage.

Trimming examines insertion records in approximate FIFO order and removes generation-matched live entries until the count reaches `maxSize` or the work budget is exhausted. Reading an entry does not refresh it, so this is not LRU behavior.

Removed and re-added values use new generation tokens. Stale queue records cannot remove the newer generation.

`ApproxCount` is maintained with atomic updates and occasionally resynchronized from the dictionary. Use it for telemetry and heuristics, not for check-then-act correctness. `Values` is a live concurrent view; `ToArray` creates a snapshot of keys observed during that call.

## Tuning

```csharp
var set = new BoundedConcurrentSet<string>(
    maxSize: 10_000,
    capacityHint: 10_000,
    trimBatchSize: 64,
    trimStartOveragePercent: 5,
    maxTrimWorkPerCall: 4_096,
    resyncAfterNoProgress: 8,
    queueOverageFactor: 4,
    comparer: StringComparer.Ordinal);
```

- `capacityHint` sets the dictionary's initial capacity; it is not another size limit.
- `trimBatchSize` is the minimum eviction work budget when trimming begins.
- `trimStartOveragePercent` controls how far above `maxSize` the approximate count may grow before an add triggers trimming. Use `0` to start immediately above `maxSize`.
- `maxTrimWorkPerCall` caps eviction work performed by one operation.
- `resyncAfterNoProgress` controls how many no-removal trim attempts occur before the approximate count is corrected from the dictionary. Use `0` to disable that correction trigger.
- `queueOverageFactor` starts bounded stale-record cleanup when the insertion queue estimate exceeds `maxSize * factor`.
- `comparer` controls value equality in the underlying concurrent dictionary.

Use this set when temporary overage and best-effort eviction are acceptable, such as telemetry suppression or bounded deduplication hints. Do not use it as an authorization boundary, durable idempotency store, exact rate limiter, or any other control where eviction timing and strict capacity are correctness requirements.
