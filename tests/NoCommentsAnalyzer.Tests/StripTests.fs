module StripTests

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open NoCommentsAnalyzer
open Xunit

let private checker = FSharpChecker.Create()

let private freshDirectory () =
    let dir =
        Path.Combine(Path.GetTempPath(), "nocomments-strip-tests", Guid.NewGuid().ToString "N")

    Directory.CreateDirectory dir |> ignore
    dir

let private stripAt (fileName: string) (source: string) =
    let sourceText = SourceText.ofString source

    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| fileName |] }

    let parseResults =
        checker.ParseFile(fileName, sourceText, parsingOptions) |> Async.RunSynchronously

    let classification = Analyzer.classify fileName sourceText parseResults.ParseTree

    let banned =
        classification.Comments
        |> List.choose (fun classified ->
            match classified.Verdict with
            | Analyzer.Banned -> Some classified.Comment.Range
            | Analyzer.Suppression
            | Analyzer.Directive -> None)

    CommentStripper.Strip.apply source banned

let private strip (source: string) =
    stripAt (Path.Combine(freshDirectory (), "Test.fs")) source

[<Fact>]
let ``a trailing comment is removed without leaving trailing whitespace`` () =
    Assert.Equal("let x = 1\n", strip "let x = 1 // trailing\n")

[<Fact>]
let ``a comment-only line is deleted entirely`` () =
    Assert.Equal("let x = 1\n", strip "// note\nlet x = 1\n")

[<Fact>]
let ``a doc comment block above a binding is deleted`` () =
    Assert.Equal("let x = 1\n", strip "/// Documents x.\n/// More detail.\nlet x = 1\n")

[<Fact>]
let ``an inline block comment keeps the surrounding tokens separated`` () =
    Assert.Equal("let f x = x\n", strip "let f(* note *)x = x\n")

[<Fact>]
let ``a multi-line block comment between bindings is deleted`` () =
    Assert.Equal("let a = 1\nlet b = 2\n", strip "let a = 1\n(* note\n   spans lines *)\nlet b = 2\n")

[<Fact>]
let ``comment lookalikes inside string literals are untouched`` () =
    let source = "let url = \"https://example.com//path\"\n"
    Assert.Equal(source, strip source)

[<Fact>]
let ``a suppression directive is preserved`` () =
    let source = "let x = 1 // fsharpanalyzer: ignore-line NOCOMMENT001\n"
    Assert.Equal(source, strip source)

[<Fact>]
let ``a configured directive prefix is preserved while prose is stripped`` () =
    let dir = freshDirectory ()

    File.WriteAllText(
        Path.Combine(dir, Config.ConfigFileName),
        """{ "directivePrefixes": ["ui-components:"] }"""
    )

    let source = "let x = 1 // ui-components: allow one-off: reason\nlet y = 2 // prose\n"
    let expected = "let x = 1 // ui-components: allow one-off: reason\nlet y = 2\n"
    Assert.Equal(expected, stripAt (Path.Combine(dir, "Test.fs")) source)

[<Fact>]
let ``crlf newlines are preserved`` () =
    Assert.Equal("let x = 1\r\nlet y = 2\r\n", strip "let x = 1 // c\r\nlet y = 2\r\n")

[<Fact>]
let ``multiple comments on one line are all removed`` () =
    Assert.Equal("let x = 1 + 2\n", strip "let x = 1 (* a *) + 2 // b\n")
