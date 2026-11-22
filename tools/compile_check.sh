#!/usr/bin/env bash
# Type-checks all Mottainai runtime + editor scripts with Unity's bundled Roslyn, without opening the project
# (works while the Unity editor is open). Usage: tools/compile_check.sh
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/Mottainai"
UVER="$(grep -m1 '^m_EditorVersion:' "$PROJ/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')"
E="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/$UVER/Editor/Data}"
OUT="$ROOT/Builds/compile_check"; mkdir -p "$OUT"
RSP="$OUT/refs.rsp"; : > "$RSP"
echo "-r:\"$(cygpath -w "$E/NetStandard/ref/2.1.0/netstandard.dll")\"" >> "$RSP"
for f in "$E"/Managed/UnityEngine/*.dll; do echo "-r:\"$(cygpath -w "$f")\"" >> "$RSP"; done
for f in "$E"/NetStandard/compat/2.1.0/shims/netstandard/System.Runtime.dll; do echo "-r:\"$(cygpath -w "$f")\"" >> "$RSP"; done
for f in "$PROJ"/Assets/_Mottainai/Plugins/*/*.dll; do echo "-r:\"$(cygpath -w "$f")\"" >> "$RSP"; done
for f in "$PROJ"/Library/ScriptAssemblies/*.dll; do
  case "$(basename "$f")" in Mottainai.*|*CodeGen*|*Tests*) continue;; esac
  echo "-r:\"$(cygpath -w "$f")\"" >> "$RSP"
done
[ -f "$E/PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll" ] && echo "-r:\"$(cygpath -w "$E/PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll")\"" >> "$RSP"
SRC=$(find "$PROJ/Assets/_Mottainai/Scripts" -name "*.cs" | while read -r f; do printf '"%s" ' "$(cygpath -w "$f")"; done)
status=0
for target in "UNITY_STANDALONE_WIN;UNITY_STANDALONE;UNITY_EDITOR" "UNITY_ANDROID"; do
  echo "== defines: $target"
  eval "\"$E/NetCoreRuntime/dotnet.exe\" \"$E/DotNetSdkRoslyn/csc.dll\" -nologo -noconfig -nostdlib -t:library -langversion:9 -nowarn:1701,1702,0649,0414,0169,0067 -unsafe -define:\"$target;UNITY_6000_0_OR_NEWER\" -out:\"$(cygpath -w "$OUT/check.dll")\" @\"$(cygpath -w "$RSP")\" $SRC" | grep -aE "error|warning CS8" | sort -u
  [ ${PIPESTATUS[0]} -ne 0 ] && status=1
done
[ $status -eq 0 ] && echo "COMPILE OK" || echo "COMPILE FAILED"
exit $status
