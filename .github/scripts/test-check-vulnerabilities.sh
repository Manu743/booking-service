#!/usr/bin/env bash
# Run the vulnerability gate regression suite: bash .github/scripts/test-check-vulnerabilities.sh
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
command -v jq >/dev/null || { echo 'jq is required for these tests.' >&2; exit 2; }
scratch="$(mktemp -d "$script_dir/.vulnerability-tests.XXXXXX")"
trap 'rm -rf -- "$scratch"' EXIT
report="$scratch/report.json"
passed=0
failed=0

check() {
  local name="$1" expected_code="$2" expected_stdout="$3" expected_stderr="$4" code=0
  bash "$script_dir/check-vulnerabilities.sh" "$report" > "$scratch/stdout" 2> "$scratch/stderr" || code=$?
  printf '%s' "$expected_stdout" > "$scratch/expected-stdout"
  printf '%s' "$expected_stderr" > "$scratch/expected-stderr"
  if [[ "$code" == "$expected_code" ]] && cmp -s "$scratch/expected-stdout" "$scratch/stdout" && cmp -s "$scratch/expected-stderr" "$scratch/stderr"; then
    printf 'PASS %s\n' "$name"
    passed=$((passed + 1))
  else
    printf 'FAIL %s (expected exit %s, got %s)\n' "$name" "$expected_code" "$code" >&2
    failed=$((failed + 1))
  fi
}

invalid() {
  check "$1" 2 '' "::error::Vulnerability report '$report' does not match the expected dotnet list package report structure."$'\n'
}

# Missing optional collections remain valid, as in real clean dotnet reports.
printf '%s\n' '{"projects":[{"path":"app.csproj"}]}' > "$report"
check 'clean report with omitted frameworks' 0 $'No vulnerable packages detected.\n' ''
printf '%s\n' '{"projects":[{"frameworks":[{"topLevelPackages":[{"vulnerabilities":[]}],"transitivePackages":[]}]}]}' > "$report"
check 'clean empty arrays' 0 $'No vulnerable packages detected.\n' ''
printf '%s\n' '{"projects":[{"frameworks":[{"topLevelPackages":[{"id":"vulnerabilities"}]}]}]}' > "$report"
check 'incidental vulnerability text' 0 $'No vulnerable packages detected.\n' ''

for collection in topLevelPackages transitivePackages; do
  jq -cn --arg collection "$collection" '{projects:[{path:"app.csproj",frameworks:[{framework:"net10.0",($collection):[{id:"Example",resolvedVersion:"1.0",vulnerabilities:[{severity:"High",advisoryurl:"https://example.com/advisory"}]}]}]}]}' > "$report"
  check "$collection advisory" 1 '' $'::error::Vulnerable packages detected (1 advisories).\napp.csproj | net10.0 | Example | 1.0 | High | https://example.com/advisory\n'
done

# Every present collection must be an array, not a falsy or wrong-typed value.
for value in false null 0 '""' '{}'; do
  jq -cn --argjson value "$value" '{projects:[{frameworks:$value}]}' > "$report"
  invalid "frameworks=$value"
  for collection in topLevelPackages transitivePackages; do
    jq -cn --arg collection "$collection" --argjson value "$value" '{projects:[{frameworks:[{($collection):$value}]}]}' > "$report"
    invalid "$collection=$value"
  done
  jq -cn --argjson value "$value" '{projects:[{frameworks:[{topLevelPackages:[{vulnerabilities:$value}]}]}]}' > "$report"
  invalid "vulnerabilities=$value"
done

printf '%s\n' '{"projects":false}' > "$report"
invalid 'invalid projects collection'
printf '{' > "$report"
check 'malformed JSON' 2 '' "::error::Vulnerability report '$report' is not valid JSON."$'\n'
: > "$report"
check 'empty report' 2 '' "::error::Vulnerability report '$report' is empty."$'\n'
rm -- "$report"
check 'missing report' 2 '' "::error::Vulnerability report '$report' was not found."$'\n'
printf '%s passed; %s failed\n' "$passed" "$failed"
[[ "$failed" == 0 ]]
