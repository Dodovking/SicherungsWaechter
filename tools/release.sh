#!/usr/bin/env bash
# Baut die EXE und veröffentlicht sie als GitHub-Release (Version aus der .csproj).
# Aufruf: tools/release.sh "Was ist neu …"
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' src/SicherungsWaechter.Gui/SicherungsWaechter.Gui.csproj)
OUT=artifacts/release
rm -rf "$OUT" && mkdir -p "$OUT"
dotnet test
dotnet publish src/SicherungsWaechter.Gui -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -o "$OUT/publish"
cp "$OUT/publish/SicherungsWaechter.exe" "$OUT/"
(cd "$OUT" && sha256sum SicherungsWaechter.exe > SicherungsWaechter.exe.sha256 && zip -q -9 SicherungsWaechter.zip SicherungsWaechter.exe)
gh release create "v$VERSION" --title "SicherungsWächter $VERSION" --notes "${1:-}" \
  "$OUT/SicherungsWaechter.exe" "$OUT/SicherungsWaechter.exe.sha256" "$OUT/SicherungsWaechter.zip"
