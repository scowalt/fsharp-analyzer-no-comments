#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet build src/NoCommentsAnalyzer -c Release
dotnet restore samples/Sample

analyzers_path="src/NoCommentsAnalyzer/bin/Release/net10.0"
mkdir -p out
rm -f out/smoke.sarif out/self.sarif

set +e
dotnet fsharp-analyzers --project samples/Sample/Sample.fsproj --analyzers-path "$analyzers_path" --report out/smoke.sarif
sample_exit=$?
set -e

if [ "$sample_exit" -eq 0 ]; then
  echo "expected a nonzero exit for the sample project"
  exit 1
fi

python3 - out/smoke.sarif <<'EOF'
import json, sys
results = json.load(open(sys.argv[1]))["runs"][0]["results"]
rules = sorted(r["ruleId"] for r in results)
assert rules == ["NOCOMMENT001", "NOCOMMENT002"], rules
EOF

dotnet fsharp-analyzers --project src/NoCommentsAnalyzer/NoCommentsAnalyzer.fsproj --analyzers-path "$analyzers_path" --report out/self.sarif

python3 - out/self.sarif <<'EOF'
import json, sys
results = json.load(open(sys.argv[1]))["runs"][0]["results"]
assert results == [], results
EOF

echo "smoke test passed"
