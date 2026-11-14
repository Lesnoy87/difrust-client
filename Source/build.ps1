param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

dotnet build (Join-Path $here "DifRUST.csproj") -c Release -p:GameDir="$GameDir"
Copy-Item (Join-Path $here "bin\Release\netstandard2.1\DifRUST.dll") (Join-Path $here "..\DifRUST.dll") -Force
Write-Host "DifRUST.dll обновлён." -ForegroundColor Green
