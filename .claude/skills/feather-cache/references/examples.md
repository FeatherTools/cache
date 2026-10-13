# Examples

All example code for this skill lives here. Examples are ordered by increasing complexity and each is self-contained. Neutral placeholder names are used throughout.

## Basic store, read, delete

Works against any `IDistributedCache`: `connection.Cache` in production, `MemoryDistributedCache` in tests.

```fs
open System
open Microsoft.Extensions.Caching.Distributed
open Feather.ErrorHandling
open Feather.Cache

let roundTrip (cache: IDistributedCache) : AsyncResult<string option * string option, string> =
    asyncResult {
        let key = CacheId "greeting"

        do! Cache.store cache key (Some (TimeSpan.FromMinutes 10.0)) "hello"
        let! value = Cache.tryGet cache key        // Some "hello"

        do! Cache.tryDelete cache key
        let! afterDelete = Cache.tryGet cache key  // None

        return value, afterDelete
    }
```

## Standalone connection

```fs
open Alma.ServiceIdentification
open Feather.Cache

// localhost:6379
let connectLocal () =
    Redis.connect (RedisConnectionConfiguration.Standalone {
        Host = Endpoint.localhost
        Port = Redis.DefaultPort
    })

let connectHost (hostname: string) =
    hostname
    |> StandaloneConfiguration.ofHostname Redis.DefaultPort
    |> RedisConnectionConfiguration.Standalone
    |> Redis.connect

let redisInstance: Instance = {
    Domain = Domain "data"
    Context = Context "redis"
    Purpose = Purpose "cache"
    Version = Version "stable"
}

// redis-cache-stable.data.svc.cluster.local:6379
let connectInCluster () =
    redisInstance
    |> StandaloneConfiguration.ofInstance Redis.DefaultPort
    |> RedisConnectionConfiguration.Standalone
    |> Redis.connect
```

`Redis.connect` throws `RedisConnectionException` if the standalone host is unreachable; call it from startup code.

## Sentinel connection

Each sentinel node is a `StandaloneConfiguration`; per-pod StatefulSet names need `ofHostname`.

```fs
open Feather.Cache

let connectSentinel (password: string option) =
    Redis.connect (RedisConnectionConfiguration.Sentinel {
        Sentinels =
            [
                "redis-node-0.redis-headless.data.svc.cluster.local"
                "redis-node-1.redis-headless.data.svc.cluster.local"
                "redis-node-2.redis-headless.data.svc.cluster.local"
            ]
            |> List.map (StandaloneConfiguration.ofHostname Redis.DefaultSentinelPort)
        ServiceName = "mymaster"   // sentinel monitor mymaster <host> <port> <quorum>
        Password = password
    })
```

## One-time token

`StringSet` writes a plain Redis String that `GetDelete` reads and removes atomically, so a second redeem gets `None`. String keys are deleted through the multiplexer, not `Cache.tryDelete`.

```fs
open System
open Feather.ErrorHandling
open Feather.Cache

let private tokenKey (token: string) = CacheId $"login-token:{token}"

let issue (connection: RedisConnection) (token: string) (userId: string) : AsyncResult<unit, string> =
    connection.StringSet (tokenKey token) (Some (TimeSpan.FromMinutes 5.0)) userId

let redeem (connection: RedisConnection) (token: string) : AsyncResult<string, string> =
    asyncResult {
        let! userId = connection.GetDelete (tokenKey token)
        return! userId |> AsyncResult.ofOption "token is unknown, expired or already used"
    }

let revoke (connection: RedisConnection) (token: string) : AsyncResult<bool, string> =
    let (CacheId key) = tokenKey token
    connection.Multiplexer.GetDatabase().KeyDeleteAsync(StackExchange.Redis.RedisKey key)
    |> AsyncResult.ofTaskCatch (fun e -> e.Message)
```

## Integration

A domain error DU wraps the library's `string` errors; the DI container creates the connection lazily, shares its `Cache` as the application's `IDistributedCache` (ASP.NET session included), and disposes the connection at shutdown.

```fs
open System
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.DependencyInjection
open Feather.ErrorHandling
open Feather.ErrorHandling.AsyncResult.Operators
open Feather.Cache

[<RequireQualifiedAccess>]
type ProfileCacheError =
    | Cache of string
    | Missing of userId: string

[<RequireQualifiedAccess>]
module ProfileCacheError =
    let format = function
        | ProfileCacheError.Cache error -> $"Profile cache failed: {error}"
        | ProfileCacheError.Missing userId -> $"Profile of {userId} is not cached"

let private profileKey userId = CacheId $"profile:{userId}"

let storeProfile (cache: IDistributedCache) (userId: string) (json: string) =
    Cache.store cache (profileKey userId) (Some (TimeSpan.FromHours 1.0)) json
    <@> ProfileCacheError.Cache

let loadProfile (cache: IDistributedCache) (userId: string) : AsyncResult<string, ProfileCacheError> =
    asyncResult {
        let! cached = Cache.tryGet cache (profileKey userId) <@> ProfileCacheError.Cache
        return! cached |> AsyncResult.ofOption (ProfileCacheError.Missing userId)
    }

let addRedisCache (configuration: RedisConnectionConfiguration) (services: IServiceCollection) =
    services
        .AddSingleton<RedisConnection>(fun _ -> Redis.connect configuration)
        .AddSingleton<IDistributedCache>(fun sp -> sp.GetRequiredService<RedisConnection>().Cache)
```

## Testing

`Cache.*` logic runs against `MemoryDistributedCache`, mirroring the library's own tests (Expecto). Consume-once logic takes the `GetDelete` function type, so an in-memory function replaces Redis.

```fs
open System
open Expecto
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Options
open Feather.ErrorHandling
open Feather.Cache

let private makeCache () =
    new MemoryDistributedCache(Options.Create(MemoryDistributedCacheOptions()))

let private runOk xR =
    xR
    |> Async.RunSynchronously
    |> function
        | Ok v -> v
        | Error err -> failtestf "Expected Ok but got Error: %s" err

[<Tests>]
let cacheTests =
    testList "Cache" [
        testCase "tryGet should return stored value when key was stored" <| fun _ ->
            let cache = makeCache ()
            runOk (Cache.store cache (CacheId "key") None "hello")

            let result = runOk (Cache.tryGet cache (CacheId "key"))

            Expect.equal result (Some "hello") "should return stored value"

        testCase "tryGet should return None when key was deleted" <| fun _ ->
            let cache = makeCache ()
            let key = CacheId "key"
            runOk (Cache.store cache key None "value")
            runOk (Cache.tryDelete cache key)

            let result = runOk (Cache.tryGet cache key)

            Expect.equal result None "should return None after deletion"
    ]

let redeem (getDelete: CacheId -> AsyncResult<string option, string>) (token: string) =
    asyncResult {
        let! userId = getDelete (CacheId $"login-token:{token}")
        return! userId |> AsyncResult.ofOption "token is unknown, expired or already used"
    }

let inMemoryGetDelete () =
    let store = Collections.Concurrent.ConcurrentDictionary<string, string>()
    let getDelete (CacheId key) : AsyncResult<string option, string> =
        match store.TryRemove key with
        | true, value -> AsyncResult.ofSuccess (Some value)
        | _ -> AsyncResult.ofSuccess None
    store, getDelete

[<Tests>]
let redeemTests =
    testCase "redeem should fail when token is redeemed twice" <| fun _ ->
        let store, getDelete = inMemoryGetDelete ()
        store["login-token:abc"] <- "user-1"

        let first = redeem getDelete "abc" |> Async.RunSynchronously
        let second = redeem getDelete "abc" |> Async.RunSynchronously

        Expect.equal first (Ok "user-1") "first redeem should return the user"
        Expect.isError second "second redeem should fail"
```
