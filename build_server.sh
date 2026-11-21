#!/usr/bin/env bash
set -e
source "$(dirname "$0")/_unity_path.sh"
case "$(uname -s)" in Linux*) M=BuildServerLinux;; *) M=BuildServerForThisOS;; esac
echo "[Mottainai] Building server ($M) with $UNITY"
"$UNITY" -batchmode -quit -projectPath "$PROJ" -executeMethod Mottainai.EditorTools.BuildTools.$M -logFile "$ROOT/Builds/build_server.log" "$@"
echo "[Mottainai] OK (see Mottainai/Builds/Server/)"
