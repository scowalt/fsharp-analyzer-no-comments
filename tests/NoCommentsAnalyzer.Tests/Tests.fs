module Tests

open System
open System.IO
open FSharp.Analyzers.SDK
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open NoCommentsAnalyzer
open Xunit

let private checker = FSharpChecker.Create()

let private analyzeAt (fileName: string) (source: string) =
    let sourceText = SourceText.ofString source

    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| fileName |] }

    let parseResults =
        checker.ParseFile(fileName, sourceText, parsingOptions) |> Async.RunSynchronously

    Analyzer.analyze fileName sourceText parseResults.ParseTree

let private freshDirectory () =
    let dir =
        Path.Combine(Path.GetTempPath(), "nocomments-tests", Guid.NewGuid().ToString "N")

    Directory.CreateDirectory dir |> ignore
    dir

let private analyze (source: string) =
    analyzeAt (Path.Combine(freshDirectory (), "Test.fs")) source

let private analyzeScript (source: string) =
    analyzeAt (Path.Combine(freshDirectory (), "Script.fsx")) source

let private codes (messages: Message list) = messages |> List.map (fun m -> m.Code)

[<Fact>]
let ``a line comment is a violation with error severity`` () =
    let messages = analyze "// note\nlet x = 1\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)
    Assert.Equal(Severity.Error, messages.Head.Severity)

[<Fact>]
let ``a trailing comment after code is a violation`` () =
    let messages = analyze "let x = 1 // trailing\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``an empty line comment is a violation`` () =
    let messages = analyze "let x = 1 //\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``slashes inside a string literal are not a comment`` () =
    let messages = analyze "let url = \"https://example.com//path\"\n"
    Assert.Empty messages

[<Fact>]
let ``block comment delimiters inside a string literal are not a comment`` () =
    let messages = analyze "let s = \"(* not a comment *)\"\n"
    Assert.Empty messages

[<Fact>]
let ``comment text inside a triple quoted string is not a comment`` () =
    let messages = analyze "let s = \"\"\"// not a comment\"\"\"\n"
    Assert.Empty messages

[<Fact>]
let ``a doc comment is a violation`` () =
    let messages = analyze "/// Documents x\nlet x = 1\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``an orphaned doc comment is a violation`` () =
    let messages = analyze "let f () =\n    /// orphan\n    1\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``a four slash comment is a violation`` () =
    let messages = analyze "//// four slashes\nlet x = 1\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``a block comment is a violation`` () =
    let messages = analyze "(* note *)\nlet x = 1\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``a multiline block comment is a violation`` () =
    let messages = analyze "(* line one\n   line two *)\nlet x = 1\n"
    Assert.NotEmpty messages
    Assert.All(messages, fun m -> Assert.Equal(Analyzer.CommentBannedCode, m.Code))

[<Fact>]
let ``a nested block comment is a violation`` () =
    let messages = analyze "(* outer (* inner *) outer *)\nlet x = 1\n"
    Assert.NotEmpty messages
    Assert.All(messages, fun m -> Assert.Equal(Analyzer.CommentBannedCode, m.Code))

[<Fact>]
let ``a shebang on line one of a script is exempt`` () =
    let messages = analyzeScript "#!/usr/bin/env -S dotnet fsi\nprintfn \"hi\"\n"
    Assert.Empty messages

[<Fact>]
let ``a comment after a shebang is a violation`` () =
    let messages = analyzeScript "#!/usr/bin/env -S dotnet fsi\n// note\nprintfn \"hi\"\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)
    Assert.Equal(2, messages.Head.Range.StartLine)

[<Fact>]
let ``a suppression directive is exempt but reported as a warning`` () =
    let messages = analyze "let x = 1 // fsharpanalyzer: ignore-line NOCOMMENT001\n"
    Assert.Equal<string list>([ Analyzer.SuppressionInUseCode ], codes messages)
    Assert.Equal(Severity.Warning, messages.Head.Severity)

[<Fact>]
let ``a block form suppression directive is exempt but reported as a warning`` () =
    let messages = analyze "(* fsharpanalyzer: ignore-file NOCOMMENT001 *)\nlet x = 1\n"
    Assert.Equal<string list>([ Analyzer.SuppressionInUseCode ], codes messages)

[<Fact>]
let ``directive prefix matching is case sensitive`` () =
    let messages = analyze "let x = 1 // FSHARPANALYZER: ignore-line NOCOMMENT001\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``a generated marker in the first comment block exempts the file`` () =
    let messages = analyze "// @generated\nlet x = 1\n// another comment\n"
    Assert.Empty messages

[<Fact>]
let ``an auto generated marker with a closing slash exempts the file`` () =
    let messages = analyze "(* <auto-generated/> *)\nlet x = 1\n// another comment\n"
    Assert.Empty messages

[<Fact>]
let ``a dotnet-style auto-generated header with a space before the slash exempts the file`` () =
    let messages = analyze "// <auto-generated />\nlet x = 1\n// another comment\n"
    Assert.Empty messages

[<Fact>]
let ``generated markers match case insensitively`` () =
    let messages = analyze "// @GENERATED\nlet x = 1\n"
    Assert.Empty messages

[<Fact>]
let ``a generated marker after code does not exempt the file`` () =
    let messages = analyze "let x = 1\n// @generated\n// another\n"

    Assert.Equal<string list>(
        [ Analyzer.CommentBannedCode; Analyzer.CommentBannedCode ],
        codes messages
    )

[<Fact>]
let ``a generated marker after a shebang exempts the file`` () =
    let messages =
        analyzeScript "#!/usr/bin/env -S dotnet fsi\n// @generated\nprintfn \"hi\"\n// note\n"

    Assert.Empty messages

[<Fact>]
let ``a file containing only comments is a violation`` () =
    let messages = analyze "// alone\n"
    Assert.Equal<string list>([ Analyzer.CommentBannedCode ], codes messages)

[<Fact>]
let ``config can add a directive prefix`` () =
    let dir = freshDirectory ()

    File.WriteAllText(
        Path.Combine(dir, Config.ConfigFileName),
        """{ "directivePrefixes": ["custom-directive:"] }"""
    )

    let messages =
        analyzeAt (Path.Combine(dir, "Test.fs")) "// custom-directive: keep\nlet x = 1\n"

    Assert.Empty messages

[<Fact>]
let ``config cannot shrink the baseline`` () =
    let dir = freshDirectory ()

    File.WriteAllText(
        Path.Combine(dir, Config.ConfigFileName),
        """{ "directivePrefixes": [], "generatedGlobs": [] }"""
    )

    let messages =
        analyzeAt
            (Path.Combine(dir, "Test.fs"))
            "let x = 1 // fsharpanalyzer: ignore-line NOCOMMENT001\n"

    Assert.Equal<string list>([ Analyzer.SuppressionInUseCode ], codes messages)

[<Fact>]
let ``malformed config is an error and the baseline applies`` () =
    let dir = freshDirectory ()
    File.WriteAllText(Path.Combine(dir, Config.ConfigFileName), "{ not json")
    let messages = analyzeAt (Path.Combine(dir, "Test.fs")) "// note\nlet x = 1\n"

    Assert.Equal<string list>(
        [ Analyzer.InvalidConfigCode; Analyzer.CommentBannedCode ],
        codes messages
    )

    Assert.Equal(Severity.Error, messages.Head.Severity)

[<Fact>]
let ``an unknown config key is an error and the baseline applies`` () =
    let dir = freshDirectory ()
    File.WriteAllText(Path.Combine(dir, Config.ConfigFileName), """{ "directivePrefix": [] }""")
    let messages = analyzeAt (Path.Combine(dir, "Test.fs")) "// note\nlet x = 1\n"

    Assert.Equal<string list>(
        [ Analyzer.InvalidConfigCode; Analyzer.CommentBannedCode ],
        codes messages
    )

[<Fact>]
let ``a wrongly typed config value is an error and the baseline applies`` () =
    let dir = freshDirectory ()

    File.WriteAllText(
        Path.Combine(dir, Config.ConfigFileName),
        """{ "directivePrefixes": "custom-directive:" }"""
    )

    let messages = analyzeAt (Path.Combine(dir, "Test.fs")) "let x = 1\n"
    Assert.Equal<string list>([ Analyzer.InvalidConfigCode ], codes messages)

[<Fact>]
let ``baseline globs exempt files under obj without any config`` () =
    let dir = freshDirectory ()
    let objDir = Path.Combine(dir, "obj", "Debug")
    Directory.CreateDirectory objDir |> ignore
    let messages = analyzeAt (Path.Combine(objDir, "Test.fs")) "// generated noise\nlet x = 1\n"
    Assert.Empty messages

[<Fact>]
let ``config can add a generated glob`` () =
    let dir = freshDirectory ()
    File.WriteAllText(Path.Combine(dir, Config.ConfigFileName), """{ "generatedGlobs": ["gen/**"] }""")
    let genDir = Path.Combine(dir, "gen")
    Directory.CreateDirectory genDir |> ignore
    let messages = analyzeAt (Path.Combine(genDir, "Test.fs")) "// generated noise\nlet x = 1\n"
    Assert.Empty messages

[<Fact>]
let ``a comment inside an inactive conditional block is not detected`` () =
    let messages = analyze "#if UNDEFINED_SYMBOL\n// hidden\n#endif\nlet x = 1\n"
    Assert.Empty messages

[<Fact>]
let ``glob patterns match path segments`` () =
    Assert.True(Glob.isMatch "**/obj/**" "src/App/obj/Debug/File.fs")
    Assert.True(Glob.isMatch "**/schema.d.ts" "schema.d.ts")
    Assert.True(Glob.isMatch "**/schema.d.ts" "src/schema.d.ts")
    Assert.False(Glob.isMatch "**/obj/**" "src/App/objects/File.fs")
    Assert.False(Glob.isMatch "gen/*" "gen/sub/File.fs")
    Assert.True(Glob.isMatch "gen/**" "gen/sub/File.fs")
