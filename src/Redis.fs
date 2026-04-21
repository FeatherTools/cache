namespace Feather.Cache

open System
open Feather.ErrorHandling
open Microsoft.Extensions.Caching.Distributed
open Alma.ServiceIdentification
open StackExchange.Redis

/// Redis connection — holds the IDistributedCache for standard ops,
/// a native getDelete using IDatabase.StringGetDeleteAsync (atomic GETDEL),
/// and a raw StringSet using IDatabase.StringSetAsync (stores plain Redis Strings
/// so they are compatible with StringGetDeleteAsync — unlike IDistributedCache
/// which writes Redis Hashes).
type RedisConnection =
    {
        Cache: IDistributedCache
        Multiplexer: IConnectionMultiplexer
        StringSet: CacheId -> TimeSpan option -> string -> AsyncResult<unit, string>
        GetDelete: CacheId -> AsyncResult<string option, string>
    }

    with
        interface IDisposable with
            member this.Dispose() =
                (this.Cache :?> IDisposable).Dispose()

[<RequireQualifiedAccess>]
type Endpoint =
    | Instance of Instance
    | Hostname of string

[<RequireQualifiedAccess>]
module Endpoint =
    let localhost = Endpoint.Hostname "localhost"

type StandaloneConfiguration = {
    Host: Endpoint
    Port: int
}

type SentinelConfiguration = {
    /// Sentinel nodes — each described the same way as a standalone instance.
    /// Sentinels typically listen on port 26379.
    Sentinels: StandaloneConfiguration list
    /// Logical primary name as configured in sentinel.conf:
    ///   sentinel monitor <ServiceName> <host> <port> <quorum>
    ServiceName: string
    /// Optional password (requirepass / masterauth). None = no auth.
    Password: string option
}

[<RequireQualifiedAccess>]
type RedisConnectionConfiguration =
    | Standalone of StandaloneConfiguration
    | Sentinel of SentinelConfiguration

[<RequireQualifiedAccess>]
module StandaloneConfiguration =
    let ofInstance port instance = { Host = Endpoint.Instance instance; Port = port }
    let ofHostname port hostname  = { Host = Endpoint.Hostname hostname; Port = port }

/// Redis-backed cache factory.
[<RequireQualifiedAccess>]
module Redis =
    open System.Threading.Tasks
    open Microsoft.Extensions.Caching.StackExchangeRedis

    /// Default Redis port.
    [<Literal>]
    let DefaultPort = 6379

    /// Default Redis Sentinel port.
    [<Literal>]
    let DefaultSentinelPort = 26379

    let private hostOf (config: StandaloneConfiguration) =
        match config.Host with
        | Endpoint.Hostname host -> host
        | Endpoint.Instance instance ->
            instance
            |> Instance.k8sLocalServiceUrl
            |> Uri
            |> fun u -> u.Host

    let private standaloneOptions (config: StandaloneConfiguration) =
        ConfigurationOptions.Parse($"{hostOf config}:{config.Port}")

    let private sentinelOptions (config: SentinelConfiguration) =
        let options = ConfigurationOptions()
        options.ServiceName        <- config.ServiceName
        options.AbortOnConnectFail <- false
        options.ConnectTimeout     <- 10_000   // ms — allow sentinels time to elect master at startup
        options.ConnectRetry       <- 5
        options.SyncTimeout        <- 500      // ms — fail fast

        config.Password |> Option.iter (fun p -> options.Password <- p)

        for sentinel in config.Sentinels do
            options.EndPoints.Add(hostOf sentinel, sentinel.Port)
        options

    let private buildMultiplexer (config: RedisConnectionConfiguration) : IConnectionMultiplexer =
        match config with
        | RedisConnectionConfiguration.Standalone c ->
            ConnectionMultiplexer.Connect(standaloneOptions c)
        | RedisConnectionConfiguration.Sentinel c ->
            ConnectionMultiplexer.Connect(sentinelOptions c)

    let connect (config: RedisConnectionConfiguration): RedisConnection =
        let multiplexer = buildMultiplexer config

        let options =
            RedisCacheOptions(
                ConnectionMultiplexerFactory = fun () -> Task.FromResult multiplexer
            )

        let cache = new RedisCache(options) :> IDistributedCache

        let getDelete (CacheId key): AsyncResult<string option, string> = asyncResult {
            let db = multiplexer.GetDatabase()
            let! (value: RedisValue) = db.StringGetDeleteAsync(RedisKey key) |> catch

            return if value.IsNullOrEmpty then None else Some (value.ToString())
        }

        let stringSet (CacheId key) (ttl: TimeSpan option) (value: string): AsyncResult<unit, string> = asyncResult {
            let db = multiplexer.GetDatabase()
            let! _ =
                match ttl with
                | Some t -> db.StringSetAsync(RedisKey key, RedisValue value, expiry = Expiration.op_Implicit(t)) |> catch
                | None   -> db.StringSetAsync(RedisKey key, RedisValue value) |> catch
            ()
        }

        {
            Cache = cache
            Multiplexer = multiplexer
            StringSet = stringSet
            GetDelete = getDelete
        }
