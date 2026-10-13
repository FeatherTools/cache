# Anti-Patterns

Format: **mistake → why it is wrong → fix**.

## Outdated / Non-existent API

- **Writing `Standalone { Instance = Some myInstance; Port = 6379 }`** → the README shows this shape, but `StandaloneConfiguration` has `Host: Endpoint` and no `Instance` field or option; it will not compile → use `{ Host = Endpoint.Instance myInstance; Port = Redis.DefaultPort }` or `StandaloneConfiguration.ofInstance Redis.DefaultPort myInstance`.

- **Copying the README Sentinel record without `Password`** → `SentinelConfiguration` has a required `Password: string option` field; the README omits it and the record will not compile → add `Password = None` (or `Some secret`).

- **Building each README sentinel entry as `{ Instance = Some sentinel0Instance; Port = 26379 }`** → same missing field as above, and an `Instance` resolves to a Service DNS name (`<context>-<purpose>-<version>.<domain>.svc.cluster.local`), not a per-pod StatefulSet name → use `StandaloneConfiguration.ofHostname Redis.DefaultSentinelPort "<pod>.<headless-svc>.<ns>.svc.cluster.local"` per node.

- **Relying on `Cache.tryDelete` returning whether the key existed** → its doc comment says "Returns true if the key existed", but it returns `AsyncResult<unit, string>` → treat it as fire-and-forget; if you need existence, call `Cache.tryGet` first or use `KeyDeleteAsync` through `connection.Multiplexer`.

- **Calling `CacheId.value` or `CacheId.create`** → `CacheId` has no companion module → construct with `CacheId "..."`, unwrap with `let (CacheId key) = id`.

## Mixing Storage Types

- **Writing with `Cache.store` and reading with `connection.GetDelete` (or `StringSet` then `Cache.tryGet`)** → `Cache.*` stores Redis Hashes, `StringSet` / `GetDelete` use Redis Strings; the wrong command on a key fails with a Redis type error → keep one family per key and prefix keys per family.

- **Removing a `StringSet` key with `Cache.tryDelete`** → `tryDelete` reads through `IDistributedCache` first, which reads a Hash → delete String keys with `connection.Multiplexer.GetDatabase().KeyDeleteAsync`, or let `GetDelete` consume them. See `examples.md` → "One-time token".

- **Implementing consume-once with `Cache.tryGet` followed by `Cache.tryDelete`** → two round trips with a race window; two callers can both read the value (`tryDelete` itself is also get-then-remove, not atomic) → use `StringSet` + `GetDelete`, which runs atomic `GETDEL`.

## Value Pitfalls

- **Storing `""` through `StringSet` and expecting `GetDelete` to return `Some ""`** → `GetDelete` maps an empty Redis value to `None`, while `Cache.tryGet` returns `Some ""` → never use an empty string as a meaningful consume-once value.

- **Passing `Some TimeSpan.Zero` or a negative TTL to `Cache.store`** → `DistributedCacheEntryOptions` throws `ArgumentOutOfRangeException` before the operation is lifted, so it escapes as an exception, not `Error` → pass `None` for no expiry and only positive TTLs.

- **Expecting sliding expiration from the TTL** → `Cache.store` sets `AbsoluteExpirationRelativeToNow`; reads do not extend it → re-`store` to extend, or use `IDistributedCache` with your own `DistributedCacheEntryOptions`.

- **Passing typed or binary payloads** → the API is `string` in and out, UTF-8 encoded → serialize (e.g. JSON) before `store` and deserialize after `tryGet`.

## Lifecycle

- **Calling `Redis.connect` per request or per operation** → every call opens a new `ConnectionMultiplexer` and connects synchronously → connect once at startup and share the `RedisConnection`.

- **Calling `Redis.connect` inside `asyncResult {}` and expecting failures as `Error`** → it is synchronous and throws `RedisConnectionException` when a standalone host is unreachable (Sentinel mode returns and keeps retrying in the background) → connect in startup code, or guard that single call with `try/with` into a `Result`.

- **Never disposing the connection, or disposing it while still in use** → `RedisConnection` owns the multiplexer → dispose once at shutdown (`use` at the top level, or a DI factory registration). Note that `Dispose` only reaches the multiplexer through `connection.Cache`, so a connection whose `Cache` was never used does not close its multiplexer.

- **Hand-building a `RedisConnection` around `MemoryDistributedCache` for tests** → `Dispose` casts `Cache` to `IDisposable`, which `MemoryDistributedCache` is not, so disposal throws `InvalidCastException` → test against `IDistributedCache` and the `GetDelete` / `StringSet` function types directly. See `examples.md` → "Testing".

## Error Handling

- **Matching on the error string or ignoring the `Result`** → the error is `sprintf "%A"` of the caught exception, an unstable diagnostic string → wrap it in a case of your own error DU with `<@>` and handle the `Error` branch at the edge.

- **Treating `Ok None` from `tryGet` / `GetDelete` as a failure inside the library call** → a missing or expired key is a normal outcome → decide at the call site, e.g. `AsyncResult.ofOption (MyError.Missing id)`.

- **Wrapping `Cache.*` calls in `try/with`** → transport and Redis errors are already caught and returned as `Error` → bind them with `let!` / `do!` in `asyncResult {}`.
