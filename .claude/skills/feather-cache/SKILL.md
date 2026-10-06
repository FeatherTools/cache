---
name: feather-cache
description: Use whenever generating or reviewing F# code that caches string values in Redis with the Feather.Cache library — connecting via `Redis.connect` with `RedisConnectionConfiguration.Standalone` / `RedisConnectionConfiguration.Sentinel`, describing hosts with `StandaloneConfiguration.ofHostname` / `StandaloneConfiguration.ofInstance`, `Endpoint.Hostname`, `Endpoint.Instance`, `Endpoint.localhost`, `SentinelConfiguration`, reading and writing through `Cache.store`, `Cache.tryGet`, `Cache.tryDelete` over `IDistributedCache`, or consume-once values via `RedisConnection.StringSet` / `RedisConnection.GetDelete`. Trigger also on mentions of `CacheId`, `RedisConnection`, `Redis.DefaultPort`, `Redis.DefaultSentinelPort`, Redis Sentinel / HA, GETDEL, one-time tokens, cache TTL / expiry, or a Redis-backed ASP.NET session or `IDistributedCache` registration.
---

# Feather-Cache

Library: [FeatherTools/cache](https://github.com/FeatherTools/cache)
NuGet: `Feather.Cache`

## Purpose

Feather.Cache wraps StackExchange.Redis and the `IDistributedCache` abstraction into a single disposable `RedisConnection` value. It connects to a standalone Redis or to a Redis Sentinel (HA) set, exposes typed `AsyncResult` cache operations over any `IDistributedCache`, and adds an atomic get-and-delete for values that must be consumed exactly once.

## When to Use

- Caching string values (serialized JSON, ids, tokens) in Redis with an optional TTL.
- Connecting an F# service to standalone Redis locally and to Redis Sentinel in Kubernetes with one configuration type.
- Consuming a value exactly once (one-time tokens, login codes) via atomic GETDEL.
- Supplying an `IDistributedCache` to ASP.NET (session, output caching) backed by Redis.
- Unit-testing cache logic against `MemoryDistributedCache` through the same `Cache` functions.

## When NOT to Use

- Binary or typed payloads — the API stores and returns `string` only; serialize yourself.
- Sliding expiration, cache tags, pub/sub, or other Redis structures — use `RedisConnection.Multiplexer` (StackExchange.Redis) directly.
- Password-protected standalone Redis — only `SentinelConfiguration` carries a `Password`.
- When you need a startup that never throws — `Redis.connect` is synchronous and raises on a failed standalone connect.

## Main Concepts

- **CacheId** — single-case `CacheId of string`; the Redis key. No companion module; unwrap with `let (CacheId key) = id`.
- **Cache** — `[<RequireQualifiedAccess>]` module over `IDistributedCache`: `store` (optional absolute TTL), `tryGet` (`string option`), `tryDelete`. Every function returns `AsyncResult<_, string>`.
- **Endpoint** — `Endpoint.Hostname of string` or `Endpoint.Instance of Instance`; `Endpoint.localhost` is `Endpoint.Hostname "localhost"`. An `Instance` resolves to `<context>-<purpose>-<version>.<domain>.svc.cluster.local` (lower-cased).
- **StandaloneConfiguration** — `{ Host: Endpoint; Port: int }`; built with `StandaloneConfiguration.ofHostname port hostname` / `ofInstance port instance`.
- **SentinelConfiguration** — `{ Sentinels: StandaloneConfiguration list; ServiceName: string; Password: string option }`; `ServiceName` must match `sentinel monitor <name>` in `sentinel.conf`.
- **RedisConnectionConfiguration** — `Standalone of StandaloneConfiguration` | `Sentinel of SentinelConfiguration`.
- **Redis.connect** — `RedisConnectionConfiguration -> RedisConnection`; connects synchronously.
- **Redis.DefaultPort** / **Redis.DefaultSentinelPort** — `6379` / `26379` literals.
- **RedisConnection** — `IDisposable` record: `Cache: IDistributedCache`, `Multiplexer: IConnectionMultiplexer`, `StringSet: CacheId -> TimeSpan option -> string -> AsyncResult<unit, string>`, `GetDelete: CacheId -> AsyncResult<string option, string>`.
- **Storage types** — `Cache.*` writes Redis Hashes (`RedisCache` behaviour); `StringSet` / `GetDelete` use plain Redis Strings. The two families do not share keys.

## Related Libraries

- **Feather.ErrorHandling** — supplies `AsyncResult`, the `asyncResult {}` computation expression and `AsyncResult.Operators` (`<@>`, `>>=`) used to compose the cache operations.
- **Alma.ServiceIdentification** — supplies `Instance` for `Endpoint.Instance` / `StandaloneConfiguration.ofInstance`.
- **Microsoft.Extensions.Caching.StackExchangeRedis** / **StackExchange.Redis** — the underlying `RedisCache` and `IConnectionMultiplexer`.
- **Microsoft.Extensions.Caching.Memory** — `MemoryDistributedCache` as an in-memory `IDistributedCache` for tests.

## Keywords for Search

Feather.Cache, FeatherTools cache, Redis, Redis Sentinel, HA, StackExchange.Redis, IDistributedCache, RedisCache, IConnectionMultiplexer, CacheId, Cache.store, Cache.tryGet, Cache.tryDelete, Redis.connect, RedisConnection, RedisConnectionConfiguration, Standalone, Sentinel, StandaloneConfiguration, ofHostname, ofInstance, SentinelConfiguration, ServiceName, Password, Endpoint, Hostname, localhost, DefaultPort, DefaultSentinelPort, StringSet, GetDelete, GETDEL, one-time token, TTL, expiry, AsyncResult, asyncResult, MemoryDistributedCache, ASP.NET session, Kubernetes, svc.cluster.local, F#

## Reference Files

- For composition principles, key-type rules, error handling, lifecycle and testing guidance, read `references/preferred-patterns.md`.
- For known pitfalls, the outdated README API, and incorrect assumptions, read `references/anti-patterns.md`.
- For worked, self-contained code examples ordered by increasing complexity, read `references/examples.md`.
