# Agent Instructions

## No code comments

This repo enforces its own policy on itself: never write comments in F# source, including `///` doc comments. Encode knowledge in names, types, tests, docs, or commit messages. The only exempt comments are machine-read directives (baseline prefix `fsharpanalyzer:`), generated-file markers, and shebangs. `./scripts/smoke-test.sh` runs the analyzer against this repo's own source and must report zero violations.

## Agent skills

### Issue tracker

Issues are tracked in this repo's GitHub Issues, using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default triage labels are used as-is (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
