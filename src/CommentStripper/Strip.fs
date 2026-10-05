module CommentStripper.Strip

open System
open FSharp.Compiler.Text

type private Line =
    { mutable Text: string
      mutable Touched: bool }

let private merge (prefix: string) (suffix: string) =
    let prefixEndsWithWhitespace =
        prefix <> "" && Char.IsWhiteSpace prefix[prefix.Length - 1]

    let suffixStartsWithWhitespace = suffix <> "" && Char.IsWhiteSpace suffix[0]

    if prefix = "" || suffix = "" then prefix + suffix
    elif prefixEndsWithWhitespace && suffixStartsWithWhitespace then prefix + suffix.TrimStart()
    elif prefixEndsWithWhitespace || suffixStartsWithWhitespace then prefix + suffix
    else prefix + " " + suffix

let private removeRange (lines: ResizeArray<Line>) (range: range) =
    let startIndex = range.StartLine - 1

    if startIndex >= 0 && startIndex < lines.Count then
        let endIndex = min (range.EndLine - 1) (lines.Count - 1)
        let startLine = lines[startIndex]
        let startText = startLine.Text
        let endText = lines[endIndex].Text
        let prefix = startText.Substring(0, min range.StartColumn startText.Length)
        let suffix = endText.Substring(min range.EndColumn endText.Length)

        startLine.Text <- merge prefix suffix
        startLine.Touched <- true

        for _ in startIndex + 1 .. endIndex do
            lines.RemoveAt(startIndex + 1)

let apply (text: string) (ranges: range list) : string =
    let newline = if text.Contains "\r\n" then "\r\n" else "\n"
    let rawLines = text.Replace("\r\n", "\n").Split '\n'

    let lines =
        ResizeArray(rawLines |> Seq.map (fun line -> { Text = line; Touched = false }))

    ranges
    |> List.sortByDescending (fun range -> range.StartLine, range.StartColumn)
    |> List.iter (removeRange lines)

    lines
    |> Seq.filter (fun line -> not (line.Touched && line.Text.Trim() = ""))
    |> Seq.map (fun line -> if line.Touched then line.Text.TrimEnd() else line.Text)
    |> String.concat newline
