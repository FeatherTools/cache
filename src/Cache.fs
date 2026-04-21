namespace Feather.Cache

type CacheId = CacheId of string

/// Cache-agnostic cache operations over IDistributedCache.
[<RequireQualifiedAccess>]
module Cache =
    open System
    open Microsoft.Extensions.Caching.Distributed
    open Feather.ErrorHandling

    /// Store a cache value. Pass None for ttl to use no expiry.
    let store (cache: IDistributedCache) (CacheId key) (ttl: TimeSpan option) (value: string): AsyncResult<unit, string> =
        asyncResult {
            let opts = DistributedCacheEntryOptions()
            ttl |> Option.iter (fun t -> opts.AbsoluteExpirationRelativeToNow <- t)
            do! cache.SetAsync(key, encode value, opts) |> catchEmpty
        }

    /// Try to retrieve a value by cache key.
    let tryGet (cache: IDistributedCache) (CacheId key) : AsyncResult<string option, string> =
        asyncResult {
            let! bytes = cache.GetAsync(key) |> catch
            return if isNull bytes then None else Some(decode bytes)
        }

    /// Try to delete a key. Returns true if the key existed.
    let tryDelete (cache: IDistributedCache) (CacheId key) : AsyncResult<unit, string> =
        asyncResult {
            let! bytes = cache.GetAsync(key) |> catch

            if isNull bytes |> not then
                do! cache.RemoveAsync(key) |> catchEmpty
        }
