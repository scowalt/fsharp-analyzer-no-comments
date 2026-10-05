module CommentStripper.Program

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open NoCommentsAnalyzer

let private checker = FSharpChecker.Create()

let private sourceExtensions = set [ ".fs"; ".fsi"; ".fsx" ]

let private skippedDirectories = set [ "obj"; "bin"; "node_modules"; ".git" ]

let rec private collectFiles (path: string) =
    if File.Exists path then
        if sourceExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) then
            [ path ]
        else
            []
    elif Directory.Exists path then
        let name = Path.GetFileName(Path.TrimEndingDirectorySeparator path)

        if skippedDirectories.Contains name then
            []
        else
            Directory.EnumerateFileSystemEntries path
            |> Seq.collect collectFiles
            |> List.ofSeq
    else
        []

let private stripFile (path: string) =
    let text = File.ReadAllText path
    let sourceText = SourceText.ofString text

    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| path |] }

    let parseResults =
        checker.ParseFile(path, sourceText, parsingOptions) |> Async.RunSynchronously

    let classification = Analyzer.classify path sourceText parseResults.ParseTree

    match classification.ConfigError with
    | Some reason -> Error reason
    | None ->
        let banned =
            classification.Comments
            |> List.choose (fun classified ->
                match classified.Verdict with
                | Analyzer.Banned -> Some classified.Comment.Range
                | Analyzer.Suppression
                | Analyzer.Directive -> None)

        if List.isEmpty banned then
            Ok false
        else
            File.WriteAllText(path, Strip.apply text banned)
            Ok true

[<EntryPoint>]
let main argv =
    if Array.isEmpty argv then
        eprintfn "usage: CommentStripper <file-or-directory>..."
        2
    else
        let files =
            argv |> Seq.collect collectFiles |> Seq.distinct |> Seq.sort |> List.ofSeq

        let mutable changed = 0
        let mutable failed = false

        for file in files do
            match stripFile file with
            | Ok true ->
                changed <- changed + 1
                printfn $"stripped {file}"
            | Ok false -> ()
            | Error reason ->
                eprintfn $"error {file}: {reason}"
                failed <- true

        printfn $"stripped comments from {changed} of {List.length files} files"
        if failed then 1 else 0
