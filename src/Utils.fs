namespace Feather.Cache

[<AutoOpen>]
module internal Utils =
    open System.Text
    open Feather.ErrorHandling

    let encode (value: string) = Encoding.UTF8.GetBytes(value)
    let decode (bytes: byte[]) = Encoding.UTF8.GetString(bytes)

    let catch task = task |> AsyncResult.ofTaskCatch (sprintf "%A")
    let catchEmpty task = task |> AsyncResult.ofEmptyTaskCatch (sprintf "%A")

    [<RequireQualifiedAccess>]
    module Instance =
        open Alma.ServiceIdentification

        let k8sLocalServiceUrl (instance: Instance) =
            sprintf "http://%s-%s-%s.%s.svc.cluster.local"
                (instance.Context |> Context.lower |> Context.value)
                (instance.Purpose |> Purpose.lower |> Purpose.value)
                (instance.Version |> Version.lower |> Version.value)
                (instance.Domain |> Domain.lower |> Domain.value)
