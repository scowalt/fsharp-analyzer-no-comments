module NoCommentsAnalyzer.Analyzer

open FSharp.Analyzers.SDK

[<Literal>]
let Code = "NOCOMMENT001"

[<CliAnalyzer("NoCommentsAnalyzer", "Bans code comments.")>]
let noCommentsAnalyzer: Analyzer<CliContext> =
    fun (_context: CliContext) -> async { return [] }
