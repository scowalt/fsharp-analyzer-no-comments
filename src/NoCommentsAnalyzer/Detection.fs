module NoCommentsAnalyzer.Detection

open FSharp.Compiler.Syntax
open FSharp.Compiler.SyntaxTrivia
open FSharp.Compiler.Text
open FSharp.Compiler.Tokenization

type LogicalComment = { Range: range; Text: string }

type FileComments =
    { Comments: LogicalComment list
      FirstCodePos: pos option }

let private triviaCommentRanges (ast: ParsedInput) =
    let comments =
        match ast with
        | ParsedInput.ImplFile(ParsedImplFileInput(trivia = trivia)) -> trivia.CodeComments
        | ParsedInput.SigFile(ParsedSigFileInput(trivia = trivia)) -> trivia.CodeComments

    comments
    |> List.map (function
        | CommentTrivia.LineComment r -> r
        | CommentTrivia.BlockComment r -> r)

let private textOfRange (sourceText: ISourceText) (r: range) =
    let clampedLine (lineNumber: int) =
        sourceText.GetLineString(min (lineNumber - 1) (sourceText.GetLineCount() - 1))

    if r.StartLine = r.EndLine then
        let line = clampedLine r.StartLine
        let endColumn = min r.EndColumn line.Length
        let startColumn = min r.StartColumn endColumn
        line.Substring(startColumn, endColumn - startColumn)
    else
        let sb = System.Text.StringBuilder()

        for lineNumber in r.StartLine .. r.EndLine do
            let line = clampedLine lineNumber

            let piece =
                if lineNumber = r.StartLine then line.Substring(min r.StartColumn line.Length)
                elif lineNumber = r.EndLine then line.Substring(0, min r.EndColumn line.Length)
                else line

            sb.Append(piece).Append('\n') |> ignore

        sb.ToString()

let private isCommentToken (info: FSharpTokenInfo) =
    info.ColorClass = FSharpTokenColorKind.Comment
    || info.CharClass = FSharpTokenCharKind.Comment
    || info.CharClass = FSharpTokenCharKind.LineComment

let private isWhitespaceToken (info: FSharpTokenInfo) =
    info.CharClass = FSharpTokenCharKind.WhiteSpace

let private scanTokens (fileName: string) (sourceText: ISourceText) (hasShebang: bool) =
    let tokenizer = FSharpSourceTokenizer([], Some fileName, None, None)
    let spans = ResizeArray<range>()
    let mutable firstCodePos: pos option = None
    let mutable state = FSharpTokenizerLexState.Initial

    for lineIndex in 0 .. sourceText.GetLineCount() - 1 do
        let lineNumber = lineIndex + 1
        let lineTokenizer = tokenizer.CreateLineTokenizer(sourceText.GetLineString lineIndex)
        let mutable currentSpan: (int * int) option = None

        let flush () =
            match currentSpan with
            | Some(startColumn, endColumn) ->
                spans.Add(
                    Range.mkRange fileName (Position.mkPos lineNumber startColumn) (Position.mkPos lineNumber endColumn)
                )

                currentSpan <- None
            | None -> ()

        let mutable scanning = true

        while scanning do
            let token, nextState = lineTokenizer.ScanToken state
            state <- nextState

            match token with
            | None -> scanning <- false
            | Some info when hasShebang && lineNumber = 1 -> ()
            | Some info when isCommentToken info ->
                match currentSpan with
                | Some(startColumn, _) -> currentSpan <- Some(startColumn, info.RightColumn + 1)
                | None -> currentSpan <- Some(info.LeftColumn, info.RightColumn + 1)
            | Some info ->
                flush ()

                if firstCodePos.IsNone && not (isWhitespaceToken info) then
                    firstCodePos <- Some(Position.mkPos lineNumber info.LeftColumn)

        flush ()

    List.ofSeq spans, firstCodePos

let collect (fileName: string) (sourceText: ISourceText) (ast: ParsedInput) : FileComments =
    let hasShebang =
        sourceText.GetLineCount() > 0 && (sourceText.GetLineString 0).StartsWith "#!"

    let triviaRanges = triviaCommentRanges ast
    let tokenSpans, firstCodePos = scanTokens fileName sourceText hasShebang

    let uncovered =
        tokenSpans
        |> List.filter (fun span -> not (triviaRanges |> List.exists (fun outer -> Range.rangeContainsRange outer span)))

    let comments =
        triviaRanges @ uncovered
        |> List.filter (fun r -> not (hasShebang && r.StartLine = 1 && r.EndLine = 1))
        |> List.sortBy (fun r -> r.StartLine, r.StartColumn)
        |> List.map (fun r -> { Range = r; Text = textOfRange sourceText r })

    { Comments = comments
      FirstCodePos = firstCodePos }
