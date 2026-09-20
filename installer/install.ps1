<#
.SYNOPSIS
    Installs a DotNotes release package, or removes an install.

.DESCRIPTION
    Ships inside the release archive and installs from beside itself, so the whole procedure is
    "unzip, run install.ps1" with nothing to download and nothing to choose.

    One archive carries both architectures. Which one a machine gets is read off the machine rather
    than off the file somebody picked, because picking is the step people get wrong -- and on
    Windows picking wrong does not fail. An x64 build runs on an ARM64 laptop under emulation, more
    slowly, and nothing says so. This product exists because its author works on one machine of each
    kind, so shipping a package that can be installed wrong on half of them would be a poor joke.

    The install is binaries only, under bin/ inside the product folder. Its siblings are the state:
    settings.json, locks/, and -- unless this machine has pointed the store at an Obsidian vault --
    the notes themselves. Nothing here writes outside bin/, and the uninstaller removes nothing else
    either. A machine-scoped note is deliberately never committed anywhere, so there is no copy to
    restore from and no uninstall flag that removes one.

    Paths use forward slashes throughout; PowerShell accepts them on Windows.

.EXAMPLE
    ./install.ps1
    Install into %LOCALAPPDATA%/BinaryVibrance/DotNotes.

.EXAMPLE
    ./install.ps1 -Register
    Install and register the server with Claude Code.

.EXAMPLE
    ./install.ps1 -Uninstall
    Remove the binaries. Notes and settings stay.
#>
[CmdletBinding()]
param(
    # Install root. Falls back to $env:DOTNOTES_DEPLOY_ROOT, then
    # %LOCALAPPDATA%/BinaryVibrance/DotNotes -- the same product folder settings.json and the default
    # note store already use, so an install and its state sit under one root instead of two.
    [string] $Destination,

    # The extracted package to install from. Defaults to beside this script, which is where it is
    # when the archive has just been unzipped.
    [string] $Source,

    # Register the server with Claude Code. Off by default: an installer that edits somebody's agent
    # configuration without being asked is doing more than installing.
    [switch] $Register,

    # The name to register under. Worth having as a parameter because a second install kept for
    # comparison has to be reachable under a second name.
    [string] $ServerName = 'dotnotes',

    [switch] $Uninstall,

    # With -Uninstall, also remove settings.json. Notes are never removed whatever this says; see
    # Remove-Install.
    [switch] $Purge
)

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/DotNotes.Deploy.ps1"

if (-not (Test-OnWindows)) { throw 'This installer is for Windows. Elsewhere, unpack the archive and run DotNotes.Server.' }

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DotNotes'

if (-not $Source) { $Source = $PSScriptRoot }

if (-not $Destination)
{
    $configured = $env:DOTNOTES_DEPLOY_ROOT
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { Join-Path $HOME 'AppData/Local' }
    $Destination = if ($configured) { $configured } else { Join-Path $localAppData 'BinaryVibrance/DotNotes' }
}

$Destination = $Destination.Replace('\', '/').TrimEnd('/')
$Source = $Source.Replace('\', '/').TrimEnd('/')

function Get-TargetRuntime
{
    <#
        The RID whose payload this machine gets. Nothing is emulated deliberately, so an
        architecture with no build is a refusal rather than a fallback.
    #>
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

    switch ($arch)
    {
        'Arm64' { return 'win-arm64' }
        'X64' { return 'win-x64' }
        default { throw "DotNotes has no build for $arch. It ships for x64 and ARM64 Windows." }
    }
}

function Assert-Payload
{
    <#
        That the package carries what this machine needs, and that it is built for the architecture
        its folder claims.

        The check is here as well as in packaging because this is the last point where the answer is
        still "the download is wrong" rather than "DotNotes is broken". An x64 payload in the
        win-arm64 folder installs and runs, so nothing downstream ever reports it.
    #>
    param(
        [Parameter(Mandatory)][string] $PayloadRoot,
        [Parameter(Mandatory)][string] $Rid
    )

    $exe = "$PayloadRoot/DotNotes.Server.exe"
    if (-not (Test-Path $exe)) { throw "the package is missing DotNotes.Server.exe for $Rid" }

    $expected = Get-ExpectedPeMachine -Rid $Rid
    $machine = Get-PeMachine $exe
    if ($machine -ne $expected)
    {
        throw ("the $Rid payload reports machine 0x{0:X4}, expected 0x{1:X4} -- this package is built wrong." -f $machine, $expected)
    }
}

function Write-ArpEntry
{
    <#
        The Add/Remove Programs entry, per-user because the install is.

        It is what makes this uninstallable by the means people actually reach for. DisplayName and
        Publisher are the strings any future package manifest has to match, and InstallLocation is
        the product root rather than bin/ so that "open the install location" lands where the
        settings and the notes are.
    #>
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Version
    )

    $bin = Get-InstallBin -Root $Root
    $sizeKb = [int](((Get-ChildItem -LiteralPath $bin -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object Length -Sum).Sum) / 1KB)

    New-Item -Path $uninstallKey -Force | Out-Null

    $native = $Root.Replace('/', '\')
    $values = @{
        DisplayName = 'DotNotes'
        DisplayVersion = $Version
        Publisher = 'BinaryVibrance'
        InstallLocation = $native
        DisplayIcon = "$native\bin\DotNotes.Server.exe"
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$native\install.ps1`" -Uninstall"
        URLInfoAbout = 'https://github.com/AtomicBlom/DotNotesMCP'
        NoModify = 1
        NoRepair = 1
        EstimatedSize = $sizeKb
    }

    foreach ($name in $values.Keys)
    {
        $kind = if ($values[$name] -is [int]) { 'DWord' } else { 'String' }
        New-ItemProperty -Path $uninstallKey -Name $name -Value $values[$name] -PropertyType $kind -Force | Out-Null
    }
}

function Register-Server
{
    <#
        Registers the stdio server with Claude Code, at user scope so it is available in every
        repository rather than in whichever one happened to be open.

        Machine-scoped notes work anywhere with no configuration, and repository-scoped notes are
        opt-in per repository through a committed file -- so one global registration is correct and
        cannot litter an unrelated repository with an untracked folder.
    #>
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][string] $Name)

    $exe = "$(Get-InstallBin -Root $Root)/DotNotes.Server.exe".Replace('/', '\')

    if (-not (Get-Command claude -ErrorAction SilentlyContinue))
    {
        Write-Warning '  claude is not on PATH; register it yourself with the command below.'
        return $false
    }

    & claude mcp add --scope user $Name -- $exe

    if ($LASTEXITCODE -ne 0)
    {
        Write-Warning "  claude mcp add exited $LASTEXITCODE; register it yourself with the command below."

        # Cleared deliberately: a warned-and-continued failure otherwise leaves $LASTEXITCODE set and
        # nothing after this point replaces it, so the script exits non-zero after installing
        # perfectly well.
        $global:LASTEXITCODE = 0

        return $false
    }

    Write-Host '  registered with Claude Code'

    return $true
}

function Remove-Install
{
    <#
        Removes the binaries, and is careful about everything else under the same root.

        Notes are never removed, and there is no flag that removes them. That is not caution for its
        own sake: the machine store exists precisely for what must not be committed, so its contents
        are the one thing on the machine with no copy in a remote, in a repository, or in anybody
        else's checkout. An uninstaller that deletes them is a data-loss bug wearing the clothes of
        a tidy-up, and -Purge on some other product having meant "remove everything" is not a reason
        to make this one mean that.

        settings.json goes only on -Purge, on the ordinary grounds that somebody uninstalling to
        install a newer build should not lose what they configured. locks/ goes either way: it is
        transient, and a lock file outliving the product that made it is just litter.
    #>
    param([Parameter(Mandatory)][string] $Root)

    if (-not (Test-Path $Root)) { Write-Host "nothing installed at $Root"; return }

    Write-Host "removing $Root"
    Stop-Install -Root $Root | Out-Null

    $notes = Get-NoteStoreLocation -Root $Root

    Clear-InstallPayload -Root $Root
    Remove-Item -LiteralPath "$Root/locks" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$Root/install.ps1" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$Root/DotNotes.Deploy.ps1" -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue

    if ($Purge)
    {
        Remove-Item -LiteralPath "$Root/settings.json" -Force -ErrorAction SilentlyContinue
        Write-Host '  removed settings.json'
    }
    else
    {
        Write-Host "  kept settings.json (use -Purge to remove it)"
    }

    # Not -Recurse, and deliberately so. The root still holds the notes whenever the store was left
    # at its default, and a recursive delete here is the one mistake in this script that could not
    # be undone. An empty directory removes; anything else stays and is reported.
    if ((Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0)
    {
        Remove-Item -LiteralPath $Root -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path $notes)
    {
        $count = @(Get-ChildItem -LiteralPath $notes -Recurse -Filter '*.md' -ErrorAction SilentlyContinue).Count
        Write-Host "  left $count note(s) at $notes"
    }

    Write-Host 'uninstalled'
}

if ($Uninstall)
{
    Remove-Install -Root $Destination

    return
}

$rid = Get-TargetRuntime
$payload = "$Source/payload/$rid"

if (-not (Test-Path $payload))
{
    throw "no $rid payload at $payload. Run this from the extracted release archive, or pass -Source."
}

Write-Host "installing DotNotes ($rid) into $Destination"

Assert-Payload -PayloadRoot $payload -Rid $rid
$version = Get-PackageVersion -PayloadRoot $payload
Write-Host "  version $version"

# Read off the payload rather than assumed, so an installer cannot be told one thing about a folder
# that contains another. Named rather than enforced: somebody installing onto a machine they are
# about to finish setting up is doing a reasonable thing.
$problems = Get-PrerequisiteProblem -PayloadRoot $payload
foreach ($problem in $problems) { Write-Warning $problem }

$stopped = Stop-Install -Root $Destination

Clear-InstallPayload -Root $Destination
$bin = Get-InstallBin -Root $Destination
New-Item -ItemType Directory -Force -Path $bin | Out-Null

Write-Host "  copying $rid payload"
Copy-Item -Path "$payload/*" -Destination $bin -Recurse -Force

# Beside the payload rather than inside it, so uninstall works from the install rather than from an
# archive somebody has since deleted -- which is what the Add/Remove Programs entry points at. They
# sit at the root because bin/ is what an install deletes and replaces.
Copy-Item -Path "$PSScriptRoot/install.ps1" -Destination "$Destination/install.ps1" -Force
Copy-Item -Path "$PSScriptRoot/DotNotes.Deploy.ps1" -Destination "$Destination/DotNotes.Deploy.ps1" -Force

Write-ArpEntry -Root $Destination -Version $version

$registered = $false
if ($Register) { $registered = Register-Server -Root $Destination -Name $ServerName }

$exe = "$bin/DotNotes.Server.exe".Replace('/', '\')

Write-Host ''
Write-Host "installed DotNotes $version to $Destination"
if (-not $registered) { Write-Host "register it with your agent:  claude mcp add --scope user $ServerName -- `"$exe`"" }
if ($stopped.ServersStopped) { Write-Host 'reconnect the MCP client with /mcp to pick this build up' }
if ($problems.Count -gt 0) { Write-Host 'install the prerequisites above before expecting the server to start' }
