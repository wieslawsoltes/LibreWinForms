#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

require_text() {
  local file="$1"
  local text="$2"
  if ! grep -qF "${text}" "${repo_root}/${file}"; then
    echo "Expected '${text}' in ${file}." >&2
    exit 1
  fi
}

require_text README.md "# LibreWinForms ProGPU Port"
require_text README.md "## Getting Started: Switch From WinForms To LibreWinForms"
require_text README.md "## NuGet Packages"
require_text README.md "LibreWinForms.Sdk"
require_text README.md "LibreWinForms.System.Windows.Forms"
require_text README.md "LibreWinForms.WindowsFormsIntegration"
require_text README.md "## Original Upstream README"
require_text docs/librewinforms-release.md "LibreWinForms.Sdk"
require_text eng/librewinforms-package-list.sh "LibreWinForms.System.Windows.Forms"
require_text eng/librewinforms-package-list.sh "LibreWinForms.WindowsFormsIntegration"
require_text eng/librewinforms-package-list.sh "LibreWinForms.Sdk"
require_text .github/workflows/librewinforms-ci.yml "LibreWinForms Build"
require_text .github/workflows/librewinforms-docs.yml "LibreWinForms Docs"
require_text .github/workflows/librewinforms-release.yml "LibreWinForms Release"

echo "LibreWinForms docs verified."
