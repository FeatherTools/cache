# Preferred Patterns

## Core Principles

- Create one `RedisConnection` per process at startup and share it. It owns the `IConnectionMultiplexer`, which is designed to be long-lived and thread-safe; dispose it once at shutdown.
- Depend on the narrowest piece you need: functions that only cache take an `IDistributedCache` (`connection.Cache`); consume-once logic takes the `GetDelete` / `StringSet` function values. This keeps them testable without Redis.
- Compose every operation inside `asyncResult {}`. All cache functions return `AsyncResult<_, string>` with the caught exception formatted as the error string; map it into your own error DU at the boundary.
- Pick the storage family per key and never mix them: `Cache.store` / `Cache.tryGet` / `Cache.tryDelete` (Redis Hash) or `StringSet` / `GetDelete` (Redis String).

## Recommended API Usage

- Plain caching with optional expiry: `Cache.store cache (CacheId key) (Some ttl) value`, `None` for no expiry. The TTL is absolute, relative to now. See `examples.md` → "Basic store, read, delete".
- Reading: `Cache.tryGet cache id` returns `Ok None` for a missing or expired key; turn a required value into an error with `AsyncResult.ofOption`.
- Removing: `Cache.tryDelete cache id` returns `Ok ()` whether or not the key existed.
- Standalone connection: `StandaloneConfiguration.ofHostname Redis.DefaultPort "localhost"` (or `Endpoint.localhost`) locally; `StandaloneConfiguration.ofInstance Redis.DefaultPort instance` for an in-cluster Service. See `examples.md` → "Standalone connection".
- Sentinel connection: list every sentinel node with `Redis.DefaultSentinelPort`, set `ServiceName` to the monitored primary name and `Password` to `Some` when `requirepass` / `masterauth` is set. See `examples.md` → "Sentinel connection".
- Consume-once values: write with `connection.StringSet`, read-and-remove atomically with `connection.GetDelete`. See `examples.md` → "One-time token".
- Anything the wrapper does not cover (key delete for String keys, counters, scans): `connection.Multiplexer.GetDatabase()`, lifting each `Task` with `AsyncResult.ofTaskCatch`.

## Error Handling

- Errors are `string` (`sprintf "%A"` of the exception). Wrap them in a case of your own `[<RequireQualifiedAccess>]` error DU with `<@>` from `Feather.ErrorHandling.AsyncResult.Operators` and give that DU a `format`. See `examples.md` → "Integration".
- A missing value is `Ok None`, not `Error`; decide per call site whether absence is an error.
- Some failures are raised, not returned: `Redis.connect` throws on a failed standalone connect, and `Cache.store` throws `ArgumentOutOfRangeException` for a zero or negative TTL. Validate TTLs before calling, and connect in startup code where an exception may terminate the process.
- Sentinel connections use `AbortOnConnectFail = false`, a 10 s connect timeout and 5 retries, so `connect` returns and the connection is established in the background; failures then appear as `Error` on each operation.

## Composition

- Build keys in one place per concept (`let private profileKey userId = CacheId $"profile:{userId}"`) so the write and read sites cannot drift.
- Bind with `let!` / `do!` in `asyncResult {}`; do not nest matches on `Result`.
- Register the connection with a DI factory so the container both creates it lazily and disposes it at shutdown, and expose `connection.Cache` as the `IDistributedCache` singleton. See `examples.md` → "Integration".

## Integration with Other Libraries

- `Endpoint.Instance` takes an `Alma.ServiceIdentification` `Instance`; the domain becomes the Kubernetes namespace and `context-purpose-version` the Service name. Use `Endpoint.Hostname` for anything else, including per-pod StatefulSet DNS names of sentinel nodes.
- `connection.Cache` is a standard `RedisCache`, so ASP.NET session and other `IDistributedCache` consumers can use it directly.
- In tests, `MemoryDistributedCache` from `Microsoft.Extensions.Caching.Memory` stands in for `connection.Cache` with the same `Cache.*` calls.

## Naming Conventions

- Prefix keys by concept (`profile:`, `login-token:`) to keep the Hash and String families on disjoint key spaces.
- `CacheId` has no `value` / `create` module; construct with `CacheId "..."` and unwrap by pattern (`let (CacheId key) = id`).
- Configuration modules are `[<RequireQualifiedAccess>]`: write `RedisConnectionConfiguration.Standalone`, `Endpoint.Hostname`, `StandaloneConfiguration.ofInstance`.

## Testing Recommendations

- Test `Cache.*` logic against a fresh `MemoryDistributedCache` per test case, as the library's own tests do. See `examples.md` → "Testing".
- Test consume-once logic by passing an in-memory `CacheId -> AsyncResult<string option, string>` in place of `connection.GetDelete`.
- Do not hand-build a `RedisConnection` around `MemoryDistributedCache`: its `Dispose` casts `Cache` to `IDisposable` and throws.
