# Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
# SPDX-License-Identifier: GPL-3.0-or-later
# Additional attribution term (GPL-3.0 section 7(b)): see NOTICE in mumax-bridge/.

# Builds mumax-bridge.exe.
#
# Prerequisites (see README.md for exact versions):
#   * Go >= 1.22            https://go.dev/dl/  (on PATH)
#   * A C compiler for cgo  (MinGW-w64 gcc on Windows, on PATH)
#   * NVIDIA CUDA Toolkit   (the version mumax3 supports; must be discoverable
#                            by mumax3's cgo flags — see README.md)
#   * mumax3 source cloned into the repo root as  <repo>\mumax3-src
#     (from this folder:  git clone https://github.com/mumax/3 ..\mumax3-src)
#
# Rule of thumb: if plain mumax3 builds on this machine, the bridge builds too,
# because it uses the identical Go + CUDA toolchain.
$ErrorActionPreference = 'Stop'
$env:CGO_ENABLED = "1"

Set-Location $PSScriptRoot

# --- Prerequisite checks: fail early with a readable message ---------------
$missing = @()
if (-not (Get-Command go  -ErrorAction SilentlyContinue)) { $missing += "go (Go >= 1.22, https://go.dev/dl/) is not on PATH" }
if (-not (Get-Command gcc -ErrorAction SilentlyContinue)) { $missing += "gcc (MinGW-w64, needed by cgo) is not on PATH" }
if (-not (Test-Path (Join-Path $PSScriptRoot "..\mumax3-src\engine"))) {
    $missing += "mumax3 source not found at ..\mumax3-src  ->  run: git clone https://github.com/mumax/3 ..\mumax3-src"
}
if (-not $env:CUDA_PATH) { Write-Warning "CUDA_PATH is not set - the build may not find the CUDA headers/libs (see README.md)." }
if ($missing.Count -gt 0) {
    $missing | ForEach-Object { Write-Error "Missing prerequisite: $_" -ErrorAction Continue }
    exit 1
}

go mod tidy
go build -v -o mumax-bridge.exe .
Write-Output "built: $PSScriptRoot\mumax-bridge.exe"
