#!/usr/bin/env bash
# Claude Code PostToolUse hook (.claude/settings.json): after an agent edits a source of a generated doc block, run
# `make docs` in the checkout that holds the file, and tell the agent which files it rewrote.
# The sources are the ones scripts/docs.py reads (openspec/specs/documentation-sync). Anything else: silent no-op.
# It never blocks: a finding is reported to the agent, and `make docs-check` in CI is still the gate.
set -u

payload=$(cat)
file=$(jq -r '.tool_input.file_path // .tool_response.filePath // empty' <<<"$payload")
[ -n "$file" ] || exit 0

# The checkout the file belongs to, so an agent's worktree regenerates its own docs and nobody else's.
root=$(git -C "$(dirname "$file")" rev-parse --show-toplevel 2>/dev/null) || exit 0
[ -f "$root/scripts/docs.py" ] || exit 0

case "${file#"$root"/}" in
  Makefile | compose/lb/nginx.conf | openspec/project.md | docs/docs-sync.toml | src/*/*.csproj | tools/*/*.csproj) ;;
  *) exit 0 ;;
esac

out=$(cd "$root" && python3 scripts/docs.py generate 2>&1)
status=$?
if [ "$status" -eq 0 ] && grep -q '^docs: 0 file(s) rewritten' <<<"$out"; then
  exit 0
fi

if [ "$status" -eq 0 ]; then
  context="make docs ran because ${file#"$root"/} is a docs source. ${out}. Review and commit those files with the change."
else
  context="make docs ran because ${file#"$root"/} is a docs source, and reported findings to fix before make docs-check passes:
${out}"
fi
jq -n --arg ctx "$context" --arg msg "$(tail -n 1 <<<"$out")" \
  '{systemMessage: $msg, hookSpecificOutput: {hookEventName: "PostToolUse", additionalContext: $ctx}}'
