#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="${repo_root}/src/test/compatibility/LibreWinForms.Portable.Tests"
manifest="${repo_root}/eng/librewinforms-portable-vector-manifest.tsv"

if [[ ! -f "${manifest}" ]]; then
  echo "Portable comparison-vector manifest is missing: ${manifest}." >&2
  exit 1
fi

actual_sources="$(find "${test_root}" -maxdepth 1 -type f -name '*BehaviorTests.cs' -printf '%f\n' | LC_ALL=C sort)"
manifest_sources="$(tail -n +2 "${manifest}" | cut -f1 | LC_ALL=C sort)"
if [[ "${actual_sources}" != "${manifest_sources}" ]]; then
  echo "Portable comparison-vector manifest does not exactly cover the frozen behavior sources." >&2
  diff -u <(printf '%s\n' "${actual_sources}") <(printf '%s\n' "${manifest_sources}") >&2 || true
  exit 1
fi

line_number=1
covered=0
migrate=0
retire=0
while IFS=$'\t' read -r source disposition owner rationale extra; do
  line_number=$((line_number + 1))
  if [[ -z "${source}" || -z "${disposition}" || -z "${owner}" || -z "${rationale}" || -n "${extra:-}" ]]; then
    echo "Malformed Portable vector manifest row ${line_number}." >&2
    exit 1
  fi
  if [[ ! -f "${test_root}/${source}" ]]; then
    echo "Portable vector manifest row ${line_number} names missing source ${source}." >&2
    exit 1
  fi
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

echo "Portable comparison-vector manifest verified: covered=${covered} migrate=${migrate} retire=${retire}."
