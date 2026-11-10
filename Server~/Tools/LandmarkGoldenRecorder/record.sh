#!/usr/bin/env bash
# Records goldens from the game's own code: record.sh <game root> [seeds, comma separated] [window cells] [out dir]
# Prints a CHECK line per compared call (bad must be 0) and writes fillet.txt, talus.txt and seat.txt: the case lists
# pasted into Server~/AlpineLib.Server.Tests/Procedural/{Meshing,Kits}/*Golden.cs. "seam" as the seeds searches for a seam-step foot.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
game="$(cd "$1" && pwd)"
seeds="${2:-1,7,42,20261111,99,123456}"
cells="${3:-40}"
out="${4:-$here/obj/out}"
mkdir -p "$here/obj/game"
landmarks="$game/Assets/Scripts/WorldGen/Landmarks"
sed -E 's/\bLandmarkTalus\b/LandmarkTalusGame/g' "$landmarks/LandmarkTalus.cs" > "$here/obj/game/LandmarkTalusGame.cs"
sed -E 's/\bLandmarkFillets\b/LandmarkFilletsGame/g' "$landmarks/LandmarkFillets.cs" > "$here/obj/game/LandmarkFilletsGame.cs"
sed -E 's/\bLandmarkLooseSeat\b/LandmarkLooseSeatGame/g; s/\bPropRotation\b/PropRotationGame/g' "$landmarks/LandmarkLooseSeat.cs" > "$here/obj/game/LandmarkLooseSeatGame.cs"
sed -E 's/\bPropRotation\b/PropRotationGame/g' "$game/Assets/Scripts/WorldGen/Props/PropRotation.cs" > "$here/obj/game/PropRotationGame.cs"
dotnet build "$here/LandmarkGoldenRecorder.csproj" -c Release -p:GameRoot="$game" -nologo -v q
dotnet "$here/bin/Release/net10.0/LandmarkGoldenRecorder.dll" "$seeds" "$cells" "$out" "$game"
