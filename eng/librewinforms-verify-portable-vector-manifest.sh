#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="${repo_root}/eng/librewinforms-portable-vector-manifest.tsv"
retired_roots=(
  "packaging/LibreWinForms.Sdk.CompatibilitySmoke"
  "src/LibreWinForms.Portable"
  "src/test/compatibility/LibreWinForms.Portable.Tests"
)

if [[ ! -f "${manifest}" ]]; then
  echo "Portable comparison-vector manifest is missing: ${manifest}." >&2
  exit 1
fi

for retired_root in "${retired_roots[@]}"; do
  if [[ -e "${repo_root}/${retired_root}" ]]; then
    echo "Retired Portable path still exists: ${retired_root}." >&2
    exit 1
  fi
done

assert_only_allowed_references() {
  local pattern="$1"
  shift
  local actual
  local expected
  actual="$(
    git -C "${repo_root}" grep -l -F -e "${pattern}" -- \
      ':!docs/**' \
      ':!eng/librewinforms-verify-portable-vector-manifest.sh' 2>/dev/null \
      | LC_ALL=C sort \
      || true
  )"
  expected="$(printf '%s\n' "$@" | LC_ALL=C sort)"
  if [[ "${actual}" != "${expected}" ]]; then
    echo "Unexpected live reference to retired Portable token '${pattern}'." >&2
    diff -u <(printf '%s\n' "${expected}") <(printf '%s\n' "${actual}") >&2 || true
    exit 1
  fi
}

assert_only_allowed_references \
  "src/LibreWinForms.Portable" \
  "eng/librewinforms-verify-docs.sh"
assert_only_allowed_references \
  "src/test/compatibility/LibreWinForms.Portable.Tests" \
  "eng/librewinforms-verify-docs.sh"
assert_only_allowed_references \
  "packaging/LibreWinForms.Sdk.CompatibilitySmoke"
assert_only_allowed_references \
  "LibreWinForms.Compatibility.System.Windows.Forms" \
  "eng/librewinforms-pack.sh" \
  "eng/librewinforms-package-smoke.sh" \
  "eng/librewinforms-verify-docs.sh"

expected_header=$'source\tdisposition\tcanonical_owner\trationale'
if [[ "$(head -n 1 "${manifest}")" != "${expected_header}" ]]; then
  echo "Portable comparison-vector manifest has an unexpected header." >&2
  exit 1
fi

line_number=1
covered=0
migrate=0
retire=0
declare -A seen_sources=()
while IFS=$'\t' read -r source disposition owner rationale extra; do
  line_number=$((line_number + 1))
  if [[ -z "${source}" || -z "${disposition}" || -z "${owner}" || -z "${rationale}" || -n "${extra:-}" ]]; then
    echo "Malformed Portable vector manifest row ${line_number}." >&2
    exit 1
  fi
  if [[ "${source}" != *BehaviorTests.cs ]]; then
    echo "Portable vector manifest row ${line_number} has unexpected vector name ${source}." >&2
    exit 1
  fi
  if [[ -n "${seen_sources[${source}]:-}" ]]; then
    echo "Portable vector manifest duplicates ${source}." >&2
    exit 1
  fi
  seen_sources["${source}"]=1
  if [[ ! -e "${repo_root}/${owner}" ]]; then
    echo "Portable vector manifest row ${line_number} names missing canonical owner ${owner}." >&2
    exit 1
  fi
  if [[ "${disposition}" == "covered" && "${owner}" == *"LibreWinForms.Portable"* ]]; then
    echo "Covered Portable vector ${source} still names a Portable owner." >&2
    exit 1
  fi
  case "${disposition}" in
    covered) covered=$((covered + 1)) ;;
    migrate) migrate=$((migrate + 1)) ;;
    retire) retire=$((retire + 1)) ;;
    *)
      echo "Portable vector manifest row ${line_number} has unknown disposition ${disposition}." >&2
      exit 1
      ;;
  esac
done < <(tail -n +2 "${manifest}")

if [[ ${covered} -ne 26 || ${migrate} -ne 0 || ${retire} -ne 0 ]]; then
  echo "Retired Portable vectors must remain at covered=26 migrate=0 retire=0." >&2
  exit 1
fi

echo "Retired Portable vector ledger verified: covered=${covered} migrate=${migrate} retire=${retire}."
