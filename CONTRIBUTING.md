# Contributing

Issues and pull requests are welcome. Include your OS, player and Discord installation type, and steps to reproduce a bug. Remove private media titles and paths from diagnostics before sharing them.

## Development

Install .NET SDK 10. The Windows GUI requires Windows 10 2004+ or Windows 11. The shared core and Linux CLI build without Windows dependencies.

```sh
dotnet build src/MusicRpc.Core/MusicRpc.Core.csproj -c Release
dotnet build src/MusicRpc.Linux/MusicRpc.Linux.csproj -c Release
dotnet run --project tests/Portable/MusicRpc.Portable.Tests.csproj -c Release
```

On Windows, also run:

```powershell
dotnet run --project tests/MusicRpc.Tests.csproj -c Release
./build.ps1
```

On Linux, test the real playerctl integration without a running music player:

```sh
dotnet publish src/MusicRpc.Linux/MusicRpc.Linux.csproj -c Release -o dist/linux
dbus-run-session -- python3 tests/Portable/mpris_smoke.py ./dist/linux/music-rpc
```

The smoke test needs `playerctl`, `dbus-run-session`, `python3-dbus`, and `python3-gi` (see CI). It creates its own session bus and synthetic player. The portable suite additionally exercises a real Unix socket when run on Linux. These checks do not prove compatibility with every AyuGram/Cider version or sandbox configuration.

Keep platform-specific code out of `MusicRpc.Core`. New readers should implement `IMediaReader` and return `Track` snapshots. Avoid adding account tokens, reading Telegram databases, or uploading local covers. Add tests when a change affects selection, timelines, metadata parsing, or IPC.

Use your own GitHub-verified commit email or your GitHub noreply address so GitHub can attribute your contributions.
