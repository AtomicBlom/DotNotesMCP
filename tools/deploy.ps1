<#
.SYNOPSIS
    Publishes DotNotes over the installed copy, or stages a release package.

.DESCRIPTION
    Two modes, because two different people run this for two different reasons.

    `deploy` promotes a build over the install so the next piece of work can be done against it, and
    that is a thing somebody does several times a day. `package` stages the tree a release is made
    of, which happens on a tag. Keeping them in one script is what makes the everyday path and the
    release path publish the same way; keeping them as separate modes is what stops the everyday
    path paying for two cross-architecture publishes.

    Either way the binaries land in `bin` inside the product folder, never the folder itself. That
    folder also holds `settings.json`, the `locks` directory and, by default, the notes themselves --
    so an install that shared it would put a deploy one careless `Remove-Item` away from deleting
    somebody's notes. Everything written here is under `bin`, and nothing it does can reach a
    sibling.

    A running MCP client holds the executable open, so a deploy stops any running server first. That
    is safe at any moment: the server holds no state a client cannot re-ask for, the notes are the
    truth, and the index rebuilds from them in tens of milliseconds.

.EXAMPLE
    ./tools/deploy.ps1
    The everyday path. Publishes Release over the install and explains what it resolved.

.EXAMPLE
    ./tools/deploy.ps1 -Configuration Debug
    Deploys a Debug build, which is worth doing when the thing being chased needs a debugger.

.EXAMPLE
    ./tools/deploy.ps1 -Mode package
    Stages both architectures and writes the release archive, ready for build-installer.ps1.
#>
[CmdletBinding()]
param(
    [ValidateSet('deploy', 'package')]
    [string] $Mode = 'deploy',

    [string] $Configuration = 'Release',

    # Where the install lives. Defaults to the product folder, and is here so a second copy can be
    # put somewhere else to compare against. `deploy` only.
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'BinaryVibrance\DotNotes\bin'),

    # The architectures a package carries. Both by default: one archive that cannot be installed on
    # the wrong machine beats two downloads and a choice. `package` only.
    [string[]] $Runtime = @('win-x64', 'win-arm64'),

    # Build a package that needs .NET 10 on the machine, about a tenth of the size.
    #
    # Ahead-of-time is the default, and for two reasons that are the same reason. A missing runtime
    # does not present as an error in a server an editor starts with no console attached -- it
    # presents as a server that is never there, and an agent that reaches for a tool and gets
    # nothing goes back to reading files and does not come back. And it starts in roughly half the
    # time, which is the other half of that rule: a slow first call loses the tool just as finally
    # as an absent one. `package` only.
    [switch] $FrameworkDependent,

    # Where the staged package goes. `package` only.
    [string] $Stage
)

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/DotNotes.Deploy.ps1"

$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src\DotNotes.Server\DotNotes.Server.csproj'

if ($Mode -eq 'deploy')
{
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

    return
}

if (-not (Test-OnWindows)) { throw 'Packaging produces a Windows release artifact.' }

function Add-VswhereToPath
{
    <#
        Puts the Visual Studio Installer directory on PATH for this process.

        vcvarsall.bat calls `vswhere` expecting to find it there, and Visual Studio's own installer
        does not put it there. What that costs is out of all proportion to what it is: vcvarsall
        writes its complaint to stderr, the ILCompiler target captures stderr along with stdout, and
        the first line of that capture is taken as the directory holding the linker. The build then
        fails with "'vswhere.exe' is not recognized ... link.exe exited with code 123" -- quoting the
        linker it did find, which reads as a broken toolchain rather than as a PATH entry.

        Harmless where it is already there, and where Visual Studio is not installed the AOT publish
        has a real prerequisite to complain about on its own.
    #>
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'

    if (-not (Test-Path (Join-Path $installer 'vswhere.exe'))) { return }
    if ($env:PATH -split ';' -contains $installer) { return }

    $env:PATH = "$env:PATH;$installer"
}

if (-not $Stage) { $Stage = Join-Path $repository 'artifacts\stage\win' }

$artifacts = Split-Path -Parent (Split-Path -Parent $Stage)

if (Test-Path $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

if (-not $FrameworkDependent) { Add-VswhereToPath }

foreach ($rid in $Runtime)
{
    if (-not (Get-ExpectedPeMachine -Rid $rid)) { throw "no packaging rule for $rid. It ships for win-x64 and win-arm64." }

    $payload = Join-Path $Stage "payload\$rid"

    # Cross-architecture is a supported path rather than a trick: the ILCompiler picks a host/target
    # pair of MSVC tools, so one machine builds both. It does need both C++ toolsets installed --
    # VC.Tools.x86.x64 and VC.Tools.ARM64 -- and says "Platform linker not found" when one is
    # missing, which reads like a broken installation rather than a missing checkbox.
    Write-Host "publishing $rid$(if ($FrameworkDependent) { ' (framework-dependent)' } else { ' (ahead-of-time)' })"

    $publish = @($project, '-c', $Configuration, '-r', $rid, '-o', $payload, '--nologo', '-p:DebugType=none')
    $publish += $(if ($FrameworkDependent) { '--self-contained', 'false' } else { '-p:PublishAot=true' })

    dotnet publish @publish

    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid with exit code $LASTEXITCODE." }

    # Neither of these is payload, and both arrive anyway. The xml is documentation for somebody
    # referencing the assemblies, generated only because IDE0005 does not run at build without it.
    # The pdb is the native symbol file, which `DebugType=none` does not suppress because that
    # setting is about managed symbols -- and at five times the size of the program it is the
    # difference between installing 15 MB and installing 85.
    Get-ChildItem -LiteralPath $payload -File -Include '*.xml', '*.pdb' -Recurse | Remove-Item -Force

    # The last point where a wrong answer is still "packaging is broken". An x64 binary staged into
    # the win-arm64 folder installs and runs emulated, so nothing downstream ever reports it.
    $exe = Join-Path $payload 'DotNotes.Server.exe'
    $expected = Get-ExpectedPeMachine -Rid $rid
    $machine = Get-PeMachine $exe

    if ($machine -ne $expected)
    {
        throw ("the $rid payload reports machine 0x{0:X4}, expected 0x{1:X4}." -f $machine, $expected)
    }
}

# Both scripts at the stage root rather than inside a payload: they are the same for either
# architecture, and the installer extracts them before it knows which payload it is laying down.
Copy-Item -Path "$PSScriptRoot/DotNotes.Deploy.ps1" -Destination $Stage -Force
Copy-Item -Path "$repository/installer/install.ps1" -Destination $Stage -Force

$version = Get-PackageVersion -PayloadRoot (Join-Path $Stage "payload\$($Runtime[0])")

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$archive = Join-Path $artifacts "dotnotes-$version-win.zip"
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }

Compress-Archive -Path "$Stage/*" -DestinationPath $archive

$size = [math]::Round((Get-Item $archive).Length / 1MB, 1)

Write-Host ''
Write-Host "staged $version at $Stage"
Write-Host "wrote  $archive (${size} MB)"
Write-Host 'build the installer from it with ./tools/build-installer.ps1'
