$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot 'artifacts/publish/win-x64'
$archivePath = Join-Path $projectRoot 'artifacts/RecotteStudioMCP-win-x64.zip'
$projectPath = Join-Path $projectRoot 'Source/RecotteStudio.McpServer/RecotteStudio.McpServer.csproj'

dotnet restore $projectPath -r win-x64 --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

dotnet publish $projectPath -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$executable = Join-Path $publishDirectory 'RecotteStudio.McpServer.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Published EXE not found.' }

Compress-Archive -LiteralPath $executable -DestinationPath $archivePath -Force
Write-Host "Created $archivePath"
