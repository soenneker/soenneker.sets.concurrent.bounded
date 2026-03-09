[![](https://img.shields.io/nuget/v/soenneker.sets.concurrent.bounded.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.sets.concurrent.bounded/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.sets.concurrent.bounded/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.sets.concurrent.bounded/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.sets.concurrent.bounded.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.sets.concurrent.bounded/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.sets.concurrent.bounded/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.sets.concurrent.bounded/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Sets.Concurrent.Bounded
### A high-performance bounded concurrent set for .NET

`Soenneker.Sets.Concurrent.Bounded` provides a **thread-safe concurrent set with an approximate maximum size**.  
It behaves similarly to a `ConcurrentDictionary`-backed set but automatically **evicts older entries when the set grows beyond a configured limit**.

Designed for **high-throughput workloads** where preventing unbounded memory growth is more important than strict eviction guarantees.

Typical uses include:

- deduplication caches
- request ID tracking
- preventing duplicate messages
- bounded concurrent caches
- rate limiting helpers
- abuse protection
- event stream deduplication

---

# Installation

```bash
dotnet add package Soenneker.Sets.Concurrent.Bounded
````

---

# Why this library exists

The .NET ecosystem lacks a simple **bounded concurrent set**.

Existing options have tradeoffs:

| Collection             | Problem                                    |
| ---------------------- | ------------------------------------------ |
| `ConcurrentDictionary` | Unbounded growth                           |
| `HashSet`              | Not thread-safe                            |
| `MemoryCache`          | Heavy for simple dedupe use cases          |
| LRU caches             | Often require locks or background eviction |

`BoundedConcurrentSet` provides a **lightweight alternative** optimized for:

* high concurrency
* predictable memory usage
* low allocation
* minimal locking

---

# Features

✔ High-throughput concurrent operations
✔ Approximate size bounding
✔ Opportunistic eviction of older entries
✔ Lock-minimized design
✔ Low allocation footprint
✔ Safe for heavy multi-threaded workloads

---

# Example

```csharp
using Soenneker.Sets.Concurrent.Bounded;

var set = new BoundedConcurrentSet<string>(maxSize: 1000);

set.TryAdd("alpha");
set.TryAdd("beta");

bool exists = set.Contains("alpha");

set.TryRemove("beta");

string[] snapshot = set.ToArray();
```

---

# Configuration

```csharp
var set = new BoundedConcurrentSet<string>(
    maxSize: 1000,
    capacityHint: 1000,
    trimBatchSize: 64,
    trimStartOveragePercent: 5
);
```

| Parameter                 | Description                                |
| ------------------------- | ------------------------------------------ |
| `maxSize`                 | Target maximum number of elements          |
| `capacityHint`            | Initial capacity hint for the dictionary   |
| `trimBatchSize`           | Number of eviction attempts per trim cycle |
| `trimStartOveragePercent` | Allowed overage before trimming begins     |

---

# Performance Characteristics

| Operation   | Complexity   |
| ----------- | ------------ |
| `TryAdd`    | O(1) average |
| `Contains`  | O(1)         |
| `TryRemove` | O(1)         |

Eviction work is **bounded per call**, preventing long latency spikes.

Internally the structure uses:

* concurrent dictionary indexing
* opportunistic FIFO-like eviction
* generation tokens to avoid stale scans
* atomic counters for approximate size tracking

---

# When to use this

Use this collection when you need:

* a **bounded concurrent set**
* **deduplication tracking**
* a **lightweight concurrent cache**
* to prevent **unbounded memory growth**

Good examples:

* deduplicating phone numbers
* tracking recent request IDs
* preventing duplicate events
* guarding against repeated API calls

---

# Thread Safety

All operations are **fully thread-safe**.

The implementation is designed for **high-concurrency environments** and does not require external locking.
