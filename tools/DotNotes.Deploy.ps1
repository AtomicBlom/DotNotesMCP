<#
.SYNOPSIS
    Everything deploy.ps1, install.ps1 and the Inno installer all have to agree about.

.DESCRIPTION
    Dot-sourced rather than imported as a module, so a caller can use these against its own
    destination without a module manifest in the way.

    Three things install DotNotes: deploy.ps1 promotes a build from source over the running copy,
    install.ps1 lays down a release package, and the Inno installer calls in here from its [Code]
    section. They replace the same files, in the same place, while the same process is holding them
    open -- so what to stop, and what is safe to delete, belongs to none of them individually.

    The deletion rules are the reason this file matters more here than the equivalent does in a
    product whose install folder holds only binaries. The product folder holds the notes, and a
    wrong answer is not a failed install but somebody's machine-scoped notes gone, with no copy
    anywhere because the whole point of that store is that it is never committed.

    Every function takes the install root explicitly. Nothing here reads a script-scope variable,
    because the callers name theirs differently and a function that reaches outward works in one of
    them by luck.
#>

# $IsWindows only exists in PowerShell 6 and up. Under Windows PowerShell 5.1 it is $null, which
# reads as false -- so every platform decision would take the non-Windows branch on Windows. The
# Inno installer shells to the 5.1 that ships with the OS, so this is the normal case rather than
# an edge one.
$script:OnWindows = if ($null -ne $IsWindows) { $IsWindows } else { $env:OS -eq 'Windows_NT' }

function Test-OnWindows
{
    return $script:OnWindows
}

function Get-HostRuntime
{
    <#
        The RID of the machine this is running on.

        OSArchitecture rather than ProcessArchitecture: PowerShell itself may be running emulated,
        and what matters is what the machine executes natively rather than what the shell happens
        to be. An x64 payload on an ARM64 machine runs under emulation and never says so.
    #>
    $arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }

    return $(if ($script:OnWindows) { "win-$arch" } else { "linux-$arch" })
}

function Get-InstallBin
{
    <#
        Where the binaries go, which is never the product root itself.

        The root also holds settings.json, locks/ and -- unless this machine has pointed the store
        at a vault -- the notes. Keeping the payload one level down is what makes "remove everything
        this install wrote" expressible as deleting one directory, rather than as a list of things
        to spare that has to stay in step with what the product writes.
    #>
    param([Parameter(Mandatory)][string] $Root)

    return (Join-Path $Root 'bin')
}

function Get-InstalledProcess
{
    <#
        Only the processes running out of the install being replaced. A server started from a
        working copy, or from a second install kept for comparison, is not in the way of this one --
        and stopping it is this script reaching outside what it was asked to replace.

        Both sides go through GetFullPath, and that is not belt and braces. A process reports the
        path it was launched with, so a server started through a subst drive, a symlink or an 8.3
        short name reports that form while the root being compared against has been normalised to
        the long one. The prefix then fails to match, nothing is found to stop, and the copy fails
        partway through on a file the server still holds open. Canonicalising one side only is what
        makes that silent rather than loud.
    #>
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Root
    )

    $full = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'

    return @(Get-Process -Name $Name -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Path -and [System.IO.Path]::GetFullPath($_.Path).StartsWith($full, [System.StringComparison]::OrdinalIgnoreCase)
        })
}

function Stop-TrackedProcess
{
    <#
        Kills processes and waits for each to actually be gone.

        Stop-Process signals a termination and returns; the process is still holding its exe and
        every assembly it mapped for a moment afterwards. Copying over the tree in that window fails
        on whichever file the loser happens to still own, which reads like a permissions problem
        rather than a race.
    #>
    param(
        [System.Diagnostics.Process[]] $Process,
        [int] $TimeoutSeconds = 10
    )

    if (-not $Process -or $Process.Count -eq 0) { return }

    $Process | Stop-Process -Force

    foreach ($one in $Process)
    {
        # A process that has already gone throws rather than returning, and that is the outcome
        # asked for either way.
        try { $null = $one.WaitForExit($TimeoutSeconds * 1000) } catch { }
    }
}

function Stop-Install
{
    <#
        Every DotNotes server running out of an install root.

        Safe to do at any moment, which is worth stating because it is not true of every server: a
        DotNotes server holds no state a client cannot re-ask for. The notes are the truth and the
        index is a cache rebuilt from them, so a killed server costs the next call a crawl of tens
        of milliseconds and nothing else.

        An indexing run is the one case worth a thought, and it survives too. A claim is a lease in
        a journal on disk, and a lease whose holder has gone is swept by the next run rather than
        stranding the note.

        Returns whether anything was stopped, so a caller can tell the user to reconnect.
    #>
    param([Parameter(Mandatory)][string] $Root)

    if (-not (Test-OnWindows) -or -not (Test-Path $Root))
    {
        return [PSCustomObject]@{ ServersStopped = $false }
    }

    $running = Get-InstalledProcess -Name 'DotNotes.Server' -Root $Root
    if ($running.Count -eq 0) { return [PSCustomObject]@{ ServersStopped = $false } }

    Write-Host "  stopping $($running.Count) server(s) running from the install (pid $($running.Id -join ', '))"
    Stop-TrackedProcess -Process $running

    return [PSCustomObject]@{ ServersStopped = $true }
}

function Clear-InstallPayload
{
    <#
        Removes the binaries an install is made of, and nothing else.

        Only bin/ is touched, and that is the single most important rule in this file. Its sibling
        notes/ is the machine store: notes that are deliberately never committed anywhere, so there
        is no copy to restore from. settings.json is what somebody chose, and locks/ is transient
        but belongs to the product rather than to this payload.

        The directory is removed rather than published over, because `publish -o` never deletes what
        it does not write -- so an assembly that has left the product would stay in the install
        forever and be loaded by a version that never shipped it.
    #>
    param([Parameter(Mandatory)][string] $Root)

    $bin = Get-InstallBin -Root $Root
    if (-not (Test-Path $bin)) { return }

    Remove-Item -LiteralPath $bin -Recurse -Force
}

function Get-NoteStoreLocation
{
    <#
        Where this machine's notes actually are, so an uninstall can say what it is leaving behind.

        settings.json may point the store at an Obsidian vault, in which case the product folder
        holds no notes at all and the honest thing to report is the vault. Read here rather than
        guessed, because telling somebody their notes are safe at a path that is not where they are
        is worse than saying nothing.

        Best effort: a settings file that cannot be read means the default, which is what the server
        would refuse over rather than silently use -- but this is a message, not a store decision.
    #>
    param([Parameter(Mandatory)][string] $Root)

    $default = Join-Path $Root 'notes'
    $settings = Join-Path $Root 'settings.json'

    if (-not (Test-Path $settings)) { return $default }

    try
    {
        $configured = (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).machineStore
        if ($configured) { return $configured }
    }
    catch
    {
        # An unreadable settings file is not a reason to say nothing about the default.
    }

    return $default
}

function Get-PrerequisiteProblem
{
    <#
        What an install needs that it cannot carry, as a list of problems -- empty when there are
        none.

        Strings rather than warnings, because the callers surface them differently: install.ps1
        writes them to the console and the Inno installer reads them back out of a redirected file
        to put in a dialog. Neither of them knows what a prerequisite is; this does.

        A self-contained payload has none, which is the reason it is the default. An MCP server is
        started by an editor with no console attached, so a missing runtime does not present as an
        error message -- it presents as a server that is simply never there, which is the one
        failure this product cannot afford: an agent that reaches for a tool and gets nothing goes
        back to reading files and does not come back.

        Nothing here is fatal. Somebody installing onto a machine they are about to finish setting
        up is doing a reasonable thing, and an installer that refuses is wrong more often than they
        are.
    #>
    param([switch] $FrameworkDependent)

    if (-not $FrameworkDependent) { return @() }

    $problems = @()

    $runtimes = @()
    try { $runtimes = @(& dotnet --list-runtimes 2>$null) } catch { }

    $core = @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.' })

    if ($runtimes.Count -eq 0)
    {
        $problems += 'No .NET runtime was found. This is the framework-dependent package, so it needs .NET 10 on the machine. Install it from https://dotnet.microsoft.com/download, or use the self-contained package, which needs nothing.'
    }
    elseif ($core.Count -eq 0)
    {
        $problems += 'No .NET 10 runtime was found. DotNotes targets net10.0. Install it from https://dotnet.microsoft.com/download, or use the self-contained package, which needs nothing.'
    }

    return $problems
}

function Get-PeMachine
{
    <#
        The machine type out of a PE header, because Test-Path cannot tell you what is in the file.

        A packaging step derives a payload's destination from one variable and its source from
        another, so the two can disagree and produce a tree that looks complete and is built for the
        wrong architecture. On Windows that does not fail: an x64 build runs emulated on ARM64, more
        slowly, and nothing says so.

        The layout: at 0x3C sits the offset of the "PE\0\0" signature, and the machine word is the
        two bytes straight after it.
    #>
    param([Parameter(Mandatory)][string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try
    {
        $reader = [System.IO.BinaryReader]::new($stream)
        $stream.Position = 0x3C
        $stream.Position = $reader.ReadInt32()
        if ($reader.ReadUInt32() -ne 0x00004550) { return $null }  # "PE\0\0"

        return $reader.ReadUInt16()
    }
    finally
    {
        $stream.Dispose()
    }
}

<#
    The PE machine word each RID must report. Shared so the packaging assertion and the installer's
    own check cannot drift into disagreeing.
#>
$script:PeMachineByRid = @{ 'win-x64' = 0x8664; 'win-arm64' = 0xAA64 }

function Get-ExpectedPeMachine
{
    param([Parameter(Mandatory)][string] $Rid)

    return $script:PeMachineByRid[$Rid]
}

function Get-PackageVersion
{
    <#
        The version out of the payload itself, so a package cannot claim a version its binaries do
        not carry. MinVer stamps it from the git tag at build time, which makes a stage
        self-describing and saves carrying a version file somebody has to keep in step.
    #>
    param([Parameter(Mandatory)][string] $PayloadRoot)

    $dll = Join-Path $PayloadRoot 'DotNotes.Server.dll'
    if (-not (Test-Path $dll)) { return '0.0.0' }

    $version = (Get-Item $dll).VersionInfo.ProductVersion
    if (-not $version) { return '0.0.0' }

    # Informational versions carry build metadata after a '+' (0.3.0+1a2b3c4). Add/Remove Programs
    # shows this string verbatim and package managers compare it, so the semver core is the useful
    # part.
    return ($version -split '\+')[0].Trim()
}
