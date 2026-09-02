#!/usr/bin/env bash

librewinforms_preview_package_ids=(
  LibreWinForms.System.Windows.Forms
  LibreWinForms.ProGPU
  LibreWinForms.WindowsFormsIntegration
  LibreWinForms.Sdk
)

librewinforms_preview_progpu_package_ids=(
  ProGPU.Backend
  ProGPU.Text.Shaping
  ProGPU.Transpiler
  ProGPU.WinRT
  ProGPU.Vector
  ProGPU.Text
  ProGPU.Compute
  ProGPU.Scene
  ProGPU.SkiaSharp
  ProGPU.System.Drawing.Common
)

# Canonical WFI is built by LibreWPF against these additional packages from the
# same pinned ProGPU source tree. They are staged from the qualified WFI handoff
# rather than rebuilt by LibreWinForms' drawing-runtime package group.
librewinforms_preview_wfi_dependency_package_ids=(
  LibreWPF.Interop
  ProGPU.DirectX
)
