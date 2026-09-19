<#
.SYNOPSIS
    Runs the enrichment loop over a note store.

.DESCRIPTION
    Starts a batch of `claude -p` sessions against the indexing mode, each enriching a handful of
    notes and exiting. The server is passed with --mcp-config rather than registered, so the
    indexing tools exist only for these processes: a session doing anything else must never be
    offered a several-hundred-iteration loop that rewrites files.

    Batching is not an optimisation. A single session enriching five hundred notes pays for its
    whole transcript on every turn and eventually compacts away the exemplars that keep the output
    consistent -- which is the one thing the whole design is buying. Resumability is what makes
    restarting free: freshness is a property of each note, so a new process picks up exactly where
    the last one stopped.

    Below roughly two hundred notes this is not worth running. Plain search over title, headings
    and body handles a store that size, and the enrichment's value is small against a description
    somebody wrote by hand.

.EXAMPLE
    ./tools/index-notes.ps1 -Store 'G:\My Drive\Obsidian\Contoso'
    Enriches a vault, in batches, until nothing is left.

.EXAMPLE
    ./tools/index-notes.ps1 -Scope repository -Batch 5 -MaxBatches 1
    One short batch over this repository's committed notes, which is how to look at the output
    before committing to a corpus.
#>
[CmdletBinding()]
param(
    # The store to enrich. Omit to use the machine store this machine is configured for.
    [string] $Store,

    # Which store, when -Store is not given.
    [ValidateSet('machine', 'repository')]
    [string] $Scope = 'machine',

    # Notes per process. Twenty keeps a transcript short enough that the exemplars survive it.
    [int] $Batch = 20,

    # A ceiling on batches, so a first run can be looked at rather than left going.
    [int] $MaxBatches = 100,

    # Reading a note and writing a structured summary against a closed vocabulary, with a validator
    # behind it, is what a small model does well. The validator does the work judgement would.
    [string] $Model = 'haiku',

    [string] $Server = (Join-Path $env:LOCALAPPDATA 'BinaryVibrance\DotNotes\bin\DotNotes.Server.exe')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Server)) { throw "No server at $Server. Run ./tools/deploy.ps1 first." }

$arguments = @('--mode', 'index', '--scope', $Scope)
if ($Store) { $arguments += @('--store', $Store) }

$config = @{
    mcpServers = @{
        'dotnotes-index' = @{ type = 'stdio'; command = $Server; args = $arguments }
    }
} | ConvertTo-Json -Depth 6 -Compress

# Only the three tools the loop needs. Rebuild is deliberately absent: it is the one that commits an
# agent to redoing a whole store, and nothing in an unattended batch should be able to reach it.
$allowed = @(
    'mcp__dotnotes-index__note_index_next'
    'mcp__dotnotes-index__note_index_write'
    'mcp__dotnotes-index__note_index_skip'
) -join ','

$prompt = @"
Enrich this note store. Call note_index_next, enrich the note it returns, call note_index_write,
and repeat. Stop after $Batch notes, or as soon as note_index_next answers drained.
Finish by saying how many you enriched and how many remain.
"@

for ($batch = 1; $batch -le $MaxBatches; $batch++)
{
    Write-Host "Batch $batch of at most $MaxBatches..."

    $output = claude -p $prompt `
        --model $Model `
        --mcp-config $config `
        --allowed-tools $allowed `
        --max-turns ($Batch * 3) 2>&1

    $output | Write-Host

    if ($LASTEXITCODE -ne 0)
    {
        Write-Warning "Batch $batch exited $LASTEXITCODE. Nothing is lost -- freshness is per note, so"
        Write-Warning 'running this again resumes where it stopped.'
        break
    }

    if ($output -match 'drained')
    {
        Write-Host 'Drained.'
        break
    }
}
