module NoCommentsAnalyzer.Glob

open System.Collections.Concurrent
open System.Text
open System.Text.RegularExpressions

let private translate (pattern: string) =
    let sb = StringBuilder()
    sb.Append '^' |> ignore
    let mutable i = 0
    let n = pattern.Length

    while i < n do
        match pattern[i] with
        | '*' when i + 1 < n && pattern[i + 1] = '*' ->
            if i + 2 < n && pattern[i + 2] = '/' then
                sb.Append "(?:.*/)?" |> ignore
                i <- i + 3
            else
                sb.Append ".*" |> ignore
                i <- i + 2
        | '*' ->
            sb.Append "[^/]*" |> ignore
            i <- i + 1
        | '?' ->
            sb.Append "[^/]" |> ignore
            i <- i + 1
        | c ->
            sb.Append(Regex.Escape(string c)) |> ignore
            i <- i + 1

    sb.Append '$' |> ignore
    sb.ToString()

let private compiled = ConcurrentDictionary<string, Regex>()

let isMatch (pattern: string) (path: string) =
    let regex =
        compiled.GetOrAdd(pattern, fun p -> Regex(translate p, RegexOptions.Compiled ||| RegexOptions.CultureInvariant))

    regex.IsMatch path
