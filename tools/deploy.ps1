<#
.SYNOPSIS
    Publishes DotNotes over the installed copy.

.DESCRIPTION
    Builds Release and copies it into the install, which is `bin` inside the product folder in local
    application data.

    The `bin` matters. That product folder also holds `settings.json`, the `locks` directory and, by
    default, the notes themselves -- so an install that shared it would put a deploy one careless
    `Remove-Item` away from deleting somebody's notes. Everything this script writes is under `bin`,
    and nothing it does can reach a sibling.

    A running MCP client holds the executable open, so the deploy stops any running server first.
    That is safe to do at any moment: the server holds no state a client cannot re-ask for, and the
    client starts a new one on the next call.

.EXAMPLE
    ./tools/deploy.ps1

.EXAMPLE
    ./tools/deploy.ps1 -Configuration Debug
    Deploys a Debug build, which is worth doing when the thing being chased needs a debugger attached.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',

    # Where the install lives. Defaults to the product folder, and is here so a second copy can be
    # put somewhere else to compare against.
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'BinaryVibrance\DotNotes\bin')
)

$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src\DotNotes.Server\DotNotes.Server.csproj'

$running = Get-Process -Name 'DotNotes.Server' -ErrorAction SilentlyContinue

if ($running)
{
    Write-Host "Stopping $($running.Count) running server(s)..."
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "Publishing $Configuration to $Destination..."
dotnet publish $project -c $Configuration -o $Destination --nologo

if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $Destination 'DotNotes.Server.exe'

Write-Host ''
& $exe --explain $repository

Write-Host ''
Write-Host 'Deployed. A client with a live session keeps talking to the server it already started;'
Write-Host 'reconnect it (/mcp in Claude Code) to pick this build up.'
