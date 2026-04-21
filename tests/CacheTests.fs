module Feather.Cache.Test

open System
open Expecto
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Options
open Feather.Cache

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private makeCache () =
    new MemoryDistributedCache(Options.Create(MemoryDistributedCacheOptions()))

let private runOk xR =
    xR
    |> Async.RunSynchronously
    |> function
        | Ok v -> v
        | Error err -> failwith $"Expected Ok but got Error: {err}"

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let cacheTests =
    testList "Cache" [
        testCase "store and retrieve a value" <| fun _ ->
            let cache = makeCache ()
            runOk (Cache.store cache (CacheId "key") None "hello")
            let result = runOk (Cache.tryGet cache (CacheId "key"))
            Expect.equal result (Some "hello") "should return stored value"

        testCase "tryGet returns None for missing key" <| fun _ ->
            let cache = makeCache ()
            let result = runOk (Cache.tryGet cache (CacheId "missing"))
            Expect.equal result None "should return None for missing key"

        testCase "store overwrites existing value" <| fun _ ->
            let cache = makeCache ()
            let key = CacheId "key"
            runOk (Cache.store cache key None "first")
            runOk (Cache.store cache key None "second")
            let result = runOk (Cache.tryGet cache key)
            Expect.equal result (Some "second") "should return updated value"

        testCase "store with TTL stores and retrieves within expiry" <| fun _ ->
            let cache = makeCache ()
            let key = CacheId "ttl-key"
            runOk (Cache.store cache key (Some (TimeSpan.FromHours 1.0)) "value")
            let result = runOk (Cache.tryGet cache key)
            Expect.equal result (Some "value") "should return value within TTL"

        testCase "tryDelete removes existing key" <| fun _ ->
            let cache = makeCache ()
            let key = CacheId "delete-key"
            runOk (Cache.store cache key None "value")
            runOk (Cache.tryDelete cache key)
            let result = runOk (Cache.tryGet cache key)
            Expect.equal result None "should return None after deletion"

        testCase "tryDelete on missing key does not fail" <| fun _ ->
            let cache = makeCache ()
            Expect.isOk
                (Cache.tryDelete cache (CacheId "nonexistent") |> Async.RunSynchronously)
                "should not fail when key does not exist"

        testCase "store handles Unicode values" <| fun _ ->
            let cache = makeCache ()
            let key = CacheId "unicode-key"
            let value = "日本語テスト 🎉"
            runOk (Cache.store cache key None value)
            let result = runOk (Cache.tryGet cache key)
            Expect.equal result (Some value) "should round-trip Unicode values"
    ]
