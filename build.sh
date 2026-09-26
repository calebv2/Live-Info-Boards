#!/usr/bin/env bash
set -euo pipefail

game_path="${1:-/home/ATT/a-township-container/game-source}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="$script_dir/bin"
mkdir -p "$output_dir"

mcs -target:library -langversion:latest \
  -out:"$output_dir/LiveInfoBoards.dll" \
  -r:"$game_path/MelonLoader/net35/MelonLoader.dll" \
  -r:"$game_path/A Township Tale_Data/Managed/Root.Township.dll" \
  -r:"$game_path/A Township Tale_Data/Managed/Alta.IO.dll" \
  -r:"$game_path/A Township Tale_Data/Managed/UnityEngine.CoreModule.dll" \
  Properties/AssemblyInfo.cs InfoBoardDocument.cs InfoBoardAuthority.cs InfoBoardDiagnosticFormatter.cs InfoBoardPlacementTracker.cs InfoBoardRegistryDocument.cs InfoBoardRegistryAuthority.cs InfoBoardRegistryCleanup.cs Core.cs
