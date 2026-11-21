#!/usr/bin/env bash
# Runs headless all-bot matches on the real Core+Gameplay sources with Unity's bundled Roslyn + .NET runtime.
# Usage: tools/sim_check.sh [runs=20] [tickRate=60]
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/Mottainai"
UVER="$(grep -m1 '^m_EditorVersion:' "$PROJ/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')"
E="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/$UVER/Editor/Data}"
OUT="$ROOT/Builds/sim_check"; mkdir -p "$OUT"
cp "$E/Managed/UnityEngine/UnityEngine.CoreModule.dll" "$E/Managed/UnityEngine/UnityEngine.SharedInternalsModule.dll" "$E/Managed/UnityEngine/UnityEngine.JSONSerializeModule.dll" "$OUT/" 2>/dev/null
FW="$(ls "$E/NetCoreRuntime/shared/Microsoft.NETCore.App/" | sort -V | tail -1)"
printf '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' "$FW" > "$OUT/sim.runtimeconfig.json"
SRC=$(find "$PROJ/Assets/_Mottainai/Scripts/Core" "$PROJ/Assets/_Mottainai/Scripts/Gameplay" "$ROOT/tools/sim_check" -name "*.cs" | while read -r f; do printf '"%s" ' "$(cygpath -w "$f")"; done)
eval "\"$E/NetCoreRuntime/dotnet.exe\" \"$E/DotNetSdkRoslyn/csc.dll\" -nologo -noconfig -nostdlib -t:exe -langversion:9 -nowarn:1701,1702,0649,0414,0169 -out:\"$(cygpath -w "$OUT/sim.dll")\" -r:\"$(cygpath -w "$E/NetStandard/ref/2.1.0/netstandard.dll")\" -r:\"$(cygpath -w "$OUT/UnityEngine.CoreModule.dll")\" -r:\"$(cygpath -w "$OUT/UnityEngine.JSONSerializeModule.dll")\" $SRC" | grep -a "error" && exit 2
"$E/NetCoreRuntime/dotnet.exe" "$(cygpath -w "$OUT/sim.dll")" "${1:-20}" "${2:-60}"
