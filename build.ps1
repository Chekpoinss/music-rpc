$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish MusicRpc.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать MusicRpc.exe' }
    Copy-Item -LiteralPath README.md -Destination dist\README.md
    Copy-Item -LiteralPath LICENSE -Destination dist\LICENSE
    Write-Host "Готово: $PSScriptRoot\dist\MusicRpc.exe"
}
finally { Pop-Location }
