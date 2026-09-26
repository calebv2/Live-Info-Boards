#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="$script_dir/bin"
mkdir -p "$output_dir"

mcs -langversion:latest \
  -out:"$output_dir/LiveInfoBoards.Tests.exe" \
  "$script_dir/Program.cs" \
  "$script_dir/../InfoBoardDocument.cs" \
  "$script_dir/../InfoBoardAuthority.cs" \
  "$script_dir/../InfoBoardDiagnosticFormatter.cs" \
  "$script_dir/../InfoBoardPlacementTracker.cs" \
  "$script_dir/../InfoBoardRegistryDocument.cs" \
  "$script_dir/../InfoBoardRegistryAuthority.cs" \
  "$script_dir/../InfoBoardRegistryCleanup.cs"

mono "$output_dir/LiveInfoBoards.Tests.exe"
