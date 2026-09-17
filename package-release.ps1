$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $projectRoot 'bin\Release'
$packageDirectory = Join-Path $projectRoot 'dist'
$packagePath = Join-Path $packageDirectory 'ChatBound.zip'
$stagingDirectory = Join-Path $packageDirectory 'ChatBound'

 dotnet build (Join-Path $projectRoot 'ChatBound.sln') -c Release

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

$dictionaryDirectory = Join-Path $stagingDirectory 'Dictionaries'
New-Item -ItemType Directory -Force -Path $dictionaryDirectory | Out-Null
Copy-Item (Join-Path $outputDirectory 'ChatBound.dll') $stagingDirectory
Copy-Item (Join-Path $outputDirectory 'ChatBound.json') $stagingDirectory
Copy-Item (Join-Path $outputDirectory 'Dictionaries\*') $dictionaryDirectory
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $packagePath
Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
Write-Output "Created $packagePath"
