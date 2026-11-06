#!/usr/bin/env bash
#
# Structural gate for the JSON report produced by:
#   dotnet list <solution> package --vulnerable --include-transitive --format json
#
# Usage: check-vulnerabilities.sh <report.json>
# Regression tests: bash .github/scripts/test-check-vulnerabilities.sh
# Optional collections may be omitted; present collections must be arrays.
#
# Exit codes:
#   0 - valid report, no vulnerable packages
#   1 - valid report, at least one vulnerable package
#   2 - missing/empty/malformed/structurally invalid report, or missing jq
#
set -uo pipefail

report="${1:-}"

fail() {
  printf '::error::%s\n' "$1" >&2
  exit 2
}

if [[ -z "$report" ]]; then
  fail "Usage: $0 <report.json>"
fi

if [[ ! -f "$report" ]]; then
  fail "Vulnerability report '$report' was not found."
fi

if [[ ! -s "$report" ]]; then
  fail "Vulnerability report '$report' is empty."
fi

if ! command -v jq >/dev/null 2>&1; then
  fail "jq is required to parse the vulnerability report but was not found on PATH."
fi

if ! jq empty "$report" >/dev/null 2>&1; then
  fail "Vulnerability report '$report' is not valid JSON."
fi

if ! jq -e '
  def optional_array(key):
    if has(key) then (.[key] | type == "array") else true end;
  type == "object"
  and (.projects | type == "array")
  and all(.projects[];
    type == "object"
    and optional_array("frameworks")
    and all((.frameworks // [])[];
      type == "object"
      and optional_array("topLevelPackages")
      and optional_array("transitivePackages")
      and all(((.topLevelPackages // []) + (.transitivePackages // []))[];
        type == "object"
        and optional_array("vulnerabilities")
      )
    )
  )
' "$report" >/dev/null 2>&1; then
  fail "Vulnerability report '$report' does not match the expected dotnet list package report structure."
fi

findings="$(
  jq -r '
    .projects[]
    | .path as $project
    | (.frameworks // [])[]
    | . as $framework
    | ((.topLevelPackages // []) + (.transitivePackages // []))[]
    | . as $package
    | ($package.vulnerabilities // [])[]
    | [
        $project,
        ($framework.framework // ""),
        ($package.id // ""),
        ($package.resolvedVersion // ""),
        (.severity // ""),
        (.advisoryurl // "")
      ]
    | join(" | ")
  ' "$report"
)" || fail "Vulnerability report '$report' has an unexpected structure."

if [[ -n "$findings" ]]; then
  count="$(printf '%s\n' "$findings" | wc -l | tr -d '[:space:]')"
  printf '::error::Vulnerable packages detected (%s advisories).\n' "$count" >&2
  printf '%s\n' "$findings" >&2
  exit 1
fi

echo "No vulnerable packages detected."
exit 0
