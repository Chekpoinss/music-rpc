#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
dotnet publish src/MusicRpc.Linux/MusicRpc.Linux.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o dist/linux
cp README.md LICENSE dist/linux/
cp packaging/linux/* dist/linux/
chmod +x dist/linux/music-rpc* dist/linux/*.sh
printf '%s\n' 'Built dist/linux/music-rpc (Linux x64)'
