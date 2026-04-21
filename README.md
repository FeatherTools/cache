# <img src="https://github.com/FeatherTools/.github/blob/main/profile/feather-logo-200.png" alt="FeatherTools Logo" width="100" height="100"> Cache

[![NuGet](https://img.shields.io/nuget/v/Feather.Cache.svg)](https://www.nuget.org/packages/Feather.Cache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Feather.Cache.svg)](https://www.nuget.org/packages/Feather.Cache)
[![Checks](https://github.com/FeatherTools/cache/actions/workflows/tests.yaml/badge.svg)](https://github.com/FeatherTools/cache/actions/workflows/tests.yaml)

> Redis-backed distributed cache library. Wraps StackExchange.Redis and `IDistributedCache` into a single `RedisConnection` value that supports both standalone Redis and Redis Sentinel (HA).

## Install

```sh
paket add Feather.Cache
```

## Connection modes

### Standalone

Single Redis instance — for local development or when HA is not required.

```fsharp
let connection =
    Redis.connect (RedisConnectionConfiguration.Standalone {
        Instance = Some myInstance  // Alma.ServiceIdentification.Instance
        Port = 6379
    })
```

### Sentinel (HA)

Three Sentinel nodes managing a primary/replica Redis set. The client always writes to and reads from the current primary — failover is transparent.

In Kubernetes, Sentinel pods expose port 26379. Pod DNS names follow the StatefulSet pattern:

```fsharp
let connection =
    Redis.connect (RedisConnectionConfiguration.Sentinel {
        Sentinels = [
            { Instance = Some sentinel0Instance; Port = 26379 }
            { Instance = Some sentinel1Instance; Port = 26379 }
            { Instance = Some sentinel2Instance; Port = 26379 }
        ]
        ServiceName = "sentinelName"  // must match `sentinel monitor <name>` in sentinel.conf
    })
```

## Cache operations

All operations are on `IDistributedCache` via `Cache` module, or directly on `RedisConnection` for atomic ops.

### Standard get/set (via `Cache` module)

```fsharp
open Feather.Cache

// Store with TTL
do! Cache.store connection.Cache (CacheId "my-key") (Some (TimeSpan.FromMinutes 10.0)) "my-value"

// Store without expiry
do! Cache.store connection.Cache (CacheId "my-key") None "my-value"

// Retrieve
let! value = Cache.tryGet connection.Cache (CacheId "my-key")
// value : string option
```

### Atomic get-and-delete

Use when a value should be consumed exactly once (e.g. one-time tokens).

```fsharp
let! value = connection.GetDelete (CacheId "one-time-token")
// value : string option — atomically removed from Redis
```

### Raw string set

Stores a plain Redis String (not a Hash), required when the value must also be read by `GetDelete`.

```fsharp
do! connection.StringSet (CacheId "my-key") (Some (TimeSpan.FromHours 1.0)) "raw-value"
```

## Notes

- `IDistributedCache` (`Cache.store` / `Cache.tryGet`) writes Redis Hashes internally (StackExchange.Redis behaviour).
- `GetDelete` and `StringSet` operate on plain Redis Strings and are mutually compatible.
- Do **not** mix `Cache.store` with `GetDelete` on the same key — they use different Redis types.
- The `RedisConnection` implements `IDisposable`; dispose it on application shutdown.

## Release
1. Increment version in `Cache.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)

### Build
```bash
./build.sh build
```

### Tests
```bash
./build.sh -t tests
```
