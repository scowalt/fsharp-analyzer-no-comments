module NoCommentsAnalyzer.Config

open System
open System.Collections.Concurrent
open System.IO
open System.Text.Json

[<Literal>]
let ConfigFileName = "no-comments.json"

type PolicyConfig =
    { DirectivePrefixes: string list
      GeneratedGlobs: string list
      ConfigDirectory: string option
      Error: string option }

let baselineDirectivePrefixes = [ "fsharpanalyzer:" ]

let baselineGeneratedGlobs =
    [ "**/obj/**"; "**/bin/**"; "**/node_modules/**"; "**/schema.d.ts" ]

let baseline =
    { DirectivePrefixes = baselineDirectivePrefixes
      GeneratedGlobs = baselineGeneratedGlobs
      ConfigDirectory = None
      Error = None }

let private readStringList (prop: JsonProperty) : Result<string list, string> =
    if prop.Value.ValueKind <> JsonValueKind.Array then
        Error $"'{prop.Name}' must be an array of strings"
    else
        let items = prop.Value.EnumerateArray() |> Seq.toList

        if items |> List.exists (fun item -> item.ValueKind <> JsonValueKind.String) then
            Error $"'{prop.Name}' must contain only strings"
        else
            Ok(items |> List.map (fun item -> item.GetString()))

let parseConfig (configPath: string) (json: string) : PolicyConfig =
    let configDirectory = Path.GetDirectoryName configPath

    let failed (reason: string) =
        { baseline with
            ConfigDirectory = Some configDirectory
            Error = Some $"{configPath}: {reason}" }

    try
        use doc = JsonDocument.Parse json
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            failed "the top-level value must be an object"
        else
            let folder state (prop: JsonProperty) =
                match state with
                | Error _ -> state
                | Ok(prefixes, globs) ->
                    match prop.Name with
                    | "directivePrefixes" -> readStringList prop |> Result.map (fun xs -> prefixes @ xs, globs)
                    | "generatedGlobs" -> readStringList prop |> Result.map (fun xs -> prefixes, globs @ xs)
                    | other -> Error $"unknown property '{other}'"

            match root.EnumerateObject() |> Seq.fold folder (Ok([], [])) with
            | Error reason -> failed reason
            | Ok(prefixes, globs) ->
                { DirectivePrefixes = baselineDirectivePrefixes @ prefixes
                  GeneratedGlobs = baselineGeneratedGlobs @ globs
                  ConfigDirectory = Some configDirectory
                  Error = None }
    with ex ->
        failed ex.Message

let private cache = ConcurrentDictionary<string, DateTime * PolicyConfig>()

let private findConfigFile (startDirectory: string) =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            None
        else
            let candidate = Path.Combine(dir.FullName, ConfigFileName)

            if File.Exists candidate then Some candidate else walk dir.Parent

    walk (DirectoryInfo startDirectory)

let load (analyzedFile: string) : PolicyConfig =
    let directory =
        try
            Path.GetDirectoryName(Path.GetFullPath analyzedFile)
        with _ ->
            null

    if String.IsNullOrEmpty directory then
        baseline
    else
        match findConfigFile directory with
        | None -> baseline
        | Some configPath ->
            let stamp = File.GetLastWriteTimeUtc configPath

            match cache.TryGetValue configPath with
            | true, (cachedStamp, cached) when cachedStamp = stamp -> cached
            | _ ->
                let parsed = parseConfig configPath (File.ReadAllText configPath)
                cache[configPath] <- (stamp, parsed)
                parsed
