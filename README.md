# DotNotesMCP

An MCP server that gives coding agents durable notes about a codebase, keyed to the **repository**
rather than the working directory, as markdown you can read and edit yourself.

## Why another one

Claude Code already has a memory. It stores notes per *worktree*, under a path-encoded directory
name, and on one real machine that has already produced:

- **Six stores for one repository**, because it has six worktrees. None can see the others, and the
  seventh worktree starts empty — which is exactly when accumulated knowledge is most wanted.
- **Two stores for another**, because `D:\...` and `d:\...` encode differently and a drive letter is
  not case-sensitive.
- **Dangling `[[wikilinks]]`** with nothing to find them, no backlinks, no search across stores, and
  a 41 KB file filed as a "memory".

None of it is shareable with a team, and none of it is visible to you outside the agent.

DotNotes fixes the keying and makes the notes yours: plain markdown with YAML frontmatter, in a
folder you can point at an Obsidian vault.

## Two stores

| Scope | Where | What belongs in it |
|---|---|---|
| `repository` | `<repo>/.dotnotes/notes/`, committed | True for anyone who clones it: how the build works, why a design went the way it did, a fixture's quirk. |
| `machine` | `%LOCALAPPDATA%\BinaryVibrance\DotNotes\notes`, or your vault | Private and never committed: local paths, machine quirks, anything naming a person or a customer. |

Scope is stated on every write and never defaulted. It is the one choice here with no undo — a
private note can be promoted, a pushed one cannot be recalled.

**Repository scope is opt-in.** It works only where the repository has committed
`.dotnotes/dotnotes.json`, so a server registered once and used everywhere never drops an untracked
folder into somebody else's repository on an agent's initiative. That same file names the
repository, so opting in and pinning the name are one action:

```json
{ "repository": "dotnotesmcp", "notes": "notes" }
```

## How a repository is identified

Reading git's layout off disk, without running `git`:

1. A committed name in `.dotnotes/dotnotes.json`.
2. The origin remote, folded so `git@github.com:You/Repo.git` and `https://github.com/You/Repo`
   agree.
3. The repository root's folder name, plus a hash of its path — because a folder name means
   something only on this machine.

A linked worktree's `.git` is a *file* pointing at a directory that carries a `commondir`, and
following it is what collapses every worktree onto one key. A submodule reaches its git directory
the same way and has no `commondir`, which is what keeps its notes its own.

## The tools

```
note_search   find notes, ranked, as one-line summaries rather than whole notes
note_read     one note whole, with what links to it
note_write    record one; scope is required
note_move     rename or move between stores, rewriting every link that pointed at it
note_delete   remove one, reporting what now links to nothing
note_check    dangling links, oversized notes, sync conflicts, duplicate names
note_context  which repository resolved and how, and where the stores are
```

## Search

There is no index store. A crawl builds a dictionary in memory on first use; at the size a personal
store reaches that is tens of milliseconds, and the notes are the truth so there is nothing that can
disagree with them.

The tokenizer is why this is written rather than taken from a library. It emits **both** the whole
identifier and the words inside it, so `GeneratedBindableCustomProperty` is found by
`bindable property`, and `Db.Primary` by `primary`. No off-the-shelf index does that without a
custom tokenizer in native code, and for a corpus whose discriminating terms are identifiers it is
worth more than everything a query engine would otherwise bring.

Ranking is BM25 over six weighted fields, then small multipliers for inbound links, recency and the
current repository's own store.

## Enrichment

A second mode where an agent — not the server — reads each note and writes back a gist, the
questions it answers, topics, entities and links. The server makes no model calls; it owns
durability, ordering and progress.

```
./tools/index-notes.ps1 -Store 'G:\My Drive\Obsidian\Vault'
```

The point is the questions. A note that only ever wrote an identifier becomes findable by somebody
typing what they actually want to know: in this repository, the query *"why did removing an
unnecessary using break the build"* scores an enriched note **73.4** against **4.9** for an
unenriched one.

Consistency is enforced rather than requested — the topic vocabulary is closed, shape is validated
by field, and the examples handed to the agent come from the store itself, which is the only
anti-drift mechanism that survives a session ending.

**Below roughly 200 notes it is not worth running.** Plain search handles a store that size.

The indexing tools are never served beside the note tools. They work, which is the problem: a
several-hundred-iteration loop that rewrites files, in front of a session doing something else,
costs a burnt session rather than an error.

## Install

### From a release

Run `dotnotes-setup.exe`, or unzip `dotnotes-<version>-win.zip` and run `install.ps1`. Both lay down
identical bytes; they differ only in whether there is a wizard.

One package carries **x64 and ARM64** and picks by reading the machine, because on Windows picking
wrong does not fail — an x64 build runs on ARM64 under emulation and nothing says so.

It is compiled ahead of time: a single 15 MB executable, an 8.9 MB installer, no runtime to install,
and it starts in about 67 ms rather than 127. Both of those are the same rule — a server that will
not start, or is slow to, is invisible to the agent that wanted it.

Tick the box on the last page to register it, or do it yourself:

```powershell
claude mcp add dotnotes --scope user -- "$env:LOCALAPPDATA\BinaryVibrance\DotNotes\bin\DotNotes.Server.exe"
```

### From source

```powershell
./tools/deploy.ps1
claude mcp add dotnotes --scope user -- "$env:LOCALAPPDATA\BinaryVibrance\DotNotes\bin\DotNotes.Server.exe"
```

### What an uninstall leaves behind

The install is `bin` *inside* the product folder, because that folder also holds your settings, the
locks and — by default — your notes. Nothing any of these write can reach a sibling.

**Uninstalling never removes your notes, and there is no flag that does.** The machine store holds
exactly what is never committed anywhere, so it is the only thing here with no copy in a remote or a
checkout. `-Purge` removes `settings.json` and nothing more.
See [the decision](docs/decisions/the-installer-cannot-remove-the-notes.md).

There is deliberately no committed `.mcp.json`: it would either pin every contributor to one
machine's install path or point at a build output that may not exist yet.

Answer what the server would answer, without starting one:

```
dotnet run --project src/DotNotes.Server -- --explain .
```

## Building

```
dotnet build DotNotes.slnx
dotnet test
dotnet format --verify-no-changes
```

Release artifacts, which need [Inno Setup](https://jrsoftware.org/isinfo.php) 6.3 or later and both
C++ toolsets — `VC.Tools.x86.x64` and `VC.Tools.ARM64`, since one machine cross-compiles both
architectures:

```powershell
./tools/deploy.ps1 -Mode package   # stage both architectures, write the zip
./tools/build-installer.ps1        # compile dotnotes-setup.exe from that same stage
```

The installer compiles from the staged tree the zip is made of rather than publishing its own, so
the two carry identical bytes. Two publishes that agree are a coincidence; one publish laid down two
ways is a guarantee.

**To cut a release, run `release.yml` from the Actions tab** and pick `patch`, `minor` or `major`.
It works the next version out from the last tag, builds, tests and packages, and only then tags the
remote and opens a **draft** release with the installer and the zip attached. Publishing that draft
is one click.

Nobody types a version, so a release cannot skip a number, reuse one or carry a typo.

The order is the point: publishing is the irreversible half, so it happens last. A failure leaves no
tag, no release and nothing to clean up — where triggering off a published release would put the
version on the page at the moment it is emptiest, and a build that then died would leave it
promising files it does not have.

The tag is created in the runner and stays local until that final step, which is what lets MinVer
stamp the binaries from the very tag about to be pushed; the run refuses if the two ever disagree.
Pick `none` to build and check everything without releasing.

The installer is unsigned, so SmartScreen warns on first download.

.NET 10. Four projects, because there is one process.
[CLAUDE.md](CLAUDE.md) has the conventions, the rules that bind everywhere, and a trigger table
pointing at the invariant that covers whatever you are about to touch. `docs/decisions/` records
what was chosen and why the alternatives lost.

## Status

Working and in use on its own repository, which is the development method rather than a nicety: if
DotNotes does not beat restating a fact next session, it has little reason to exist.

Not yet done: importing the existing `.claude/projects/*/memory/` stores, and an HTTP transport.

## Licence

Apache-2.0.
