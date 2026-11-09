#!/usr/bin/env bash
# 코어 DLL과 게임 데이터를 Unity 프로젝트에 넣는다. 코어나 데이터를 고친 뒤 다시 실행한다.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
dotnet build "$root/src/Sam11.Core" -c Release -nologo -v q
mkdir -p "$root/Unity/Assets/Plugins/Sam11" "$root/Unity/Assets/StreamingAssets"
cp "$root/src/Sam11.Core/bin/Release/netstandard2.1/Sam11.Core.dll" "$root/Unity/Assets/Plugins/Sam11/"
rm -rf "$root/Unity/Assets/StreamingAssets/data" "$root/Unity/Assets/StreamingAssets/Mods"
cp -r "$root/data" "$root/Unity/Assets/StreamingAssets/data"
if [ -d "$root/Mods" ]; then cp -r "$root/Mods" "$root/Unity/Assets/StreamingAssets/Mods"; fi
echo "Unity 프로젝트에 코어와 데이터를 넣었다: $root/Unity"
