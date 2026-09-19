# DotNotesMCP

An MCP server that gives coding agents durable notes about a codebase, keyed to the **repository**
rather than the working directory, in two stores: one committed with the code, one private to this
machine and pointable at an Obsidian vault. It replaces a memory that fragments across worktrees --
Loom's is split over six directories, RoseMCP's over eight, and `D--Contoso-Platform` and
`d--Contoso-Platform` are two stores for one repository because a drive letter is not case-sensitive.

## Architecture

```
client --stdio--> DotNotes.Server            (default mode: seven note_* tools)
                     |
                     +--> machine store   %LOCALAPPDATA%\BinaryVibrance\DotNotes\notes
                     |                    or an Obsidian vault, per machine
                     +--> repository store  <repo root>/.dotnotes/notes/   (committed)

client --stdio--> DotNotes.Server --mode index   (five note_index_* tools and nothing else)
```

| Project | What it is |
|---|---|
| `DotNotes.Contracts` | Result records, tool names, tool descriptions, argument parsing. No package references at all, which is what lets every host and every test reference it, and lets the model-facing surface be measured without building a server. |
| `DotNotes.Notes` | Repository identity, configuration, store routing, frontmatter, links, the store lock. Reads git's layout off disk without running `git`, so deciding *which* store a call means does not depend on the tool that operates one. |
| `DotNotes.Index` | The crawler, the tokenizer, BM25, snippets, and the indexing loop. There is no index store: the index is a dictionary built on first use, because the notes are the truth and rebuilding from them is tens of milliseconds. |
| `DotNotes.Server` | The console host and the single `AddDotNotes()` registration path. |

Four projects because there is one process. Rose splits eighteen ways, and every one of those splits
is forced by a process boundary, a Windows-only target framework or a native toolchain -- none of
which exists here. See [the decision](docs/decisions/four-projects-because-there-is-one-process.md).

## Rules that bind everywhere

These are the ones you can break without going anywhere near the subsystem that owns them.

- **An agent reaches for what it knows, so a tool has to say when it is the better choice -- and
  then be stable enough to stay chosen.** Two halves that fail together. Every description names
  what the caller would otherwise have done. And stability is a correctness property rather than a
  quality-of-service one: one empty answer, one unexplained refusal, one slow first call, and the
  agent goes back to reading files and does not return. A degraded store **refuses out loud and
  names the fix** rather than answering with nothing.
- **Say it in as few tokens as will produce the right behaviour.** The model-facing surface is paid
  for at the start of every session, before a single call. That is a budget on the total rather than
  an allowance per tool, and the question for every sentence is whether removing it changes what the
  agent does. Reasoning goes in doc comments, which a maintainer reads and a model never does.
- **Nothing writes to stdout in stdio mode** except protocol frames. All logging goes to stderr. A
  stray `Console.WriteLine` corrupts the stream, and the failure looks like a protocol bug rather
  than a print statement.
- **A note is keyed to the repository, never to the worktree and never to the working directory.**
  Resolution happens once, in `RepositoryIdentity.For`, and every store path derives from its
  answer. A path used directly is a store that fragments six ways -- the defect this server exists
  to remove.
- **A note's scope is stated, never inferred.** There is no default on a write. Guessing wrong
  commits a machine-specific fact to a shared repository, and a pushed note cannot be recalled.
- **Every result names the scope and the repository that answered.** Attribution is added once, in
  `NoteService`, so a tool added later cannot forget. There is no process hop to enforce it, so the
  test over every registered tool is the part that actually holds.
- **The notes on disk are the truth; the index is a cache.** Every read parses what is there, and
  the index rebuilds from the notes alone with no model calls.
- **A file the person may have edited is never silently overwritten.** Writes are whole-file, to a
  temp name in the same directory, then moved over. A write whose content is unchanged does not
  happen at all: on a synced store a no-op write is a round trip and a revision.
- **An error says what went wrong, not that something did.** Convert at the MCP boundary, never at
  the throw site: the exception type carries meaning further in, and retry decisions turn on it.

## Where things are written down

- **Decisions** go in `docs/decisions/`, one file per decision, named for the decision rather than
  numbered: what was chosen, and why the alternatives lost.
- **Invariants** go in `docs/invariants/`: a rule a change could break, and the failure it prevents.

## Commands

```
dotnet build DotNotes.slnx
dotnet test
dotnet format                      # run before every commit
dotnet format --verify-no-changes  # what CI checks
```

**Never pass `--nologo` to `dotnet test` here.** It is a VSTest option, Microsoft.Testing.Platform
does not recognise it, and an unrecognised option is reported as `Zero tests ran` with exit code 5,
which reads exactly like a discovery failure. The banner-suppressing equivalent is `--no-banner`.
`dotnet test` needs the `global.json` opt-in already in the repo, because TUnit runs on
Microsoft.Testing.Platform and the .NET 10 SDK no longer bridges that through VSTest. The test
project is also an executable, so running it directly works and is faster:

```
./tests/DotNotes.UnitTests/bin/Debug/net10.0/DotNotes.UnitTests.exe
```

Answer what the server would answer, without starting one:

```
dotnet run --project src/DotNotes.Server -- --explain .
```

## Dogfooding is the point, not a nicety

This repository is the first consumer of its own notes. **If DotNotes does not beat restating a fact
next session, it has little reason to exist**, and the only way to know is to use it here.

- **What would have gone to Claude's memory goes here instead**, in this repository's own
  `.dotnotes/notes/`, committed.
- **A note nobody reaches for is a bug of the same severity as one that returns the wrong note.** If
  the tool exists, works, and still lost to reading the files, the reason it lost is the finding --
  usually the description, the argument shape, or an answer that was empty when it should have
  refused.

## Conventions

Enforced by `.editorconfig` where the analyzer can express them, by review where it cannot.

- **Tabs**, not spaces.
- **File-scoped namespaces**, matching the folder they live in (IDE0130).
- **Braces on their own line** -- Allman, everywhere.
- **A body on its own line gets braces.** A single simple statement kept on the same line as the
  condition may go without them; anything that wraps is braced.
- **Readable `if` statements.** Prefer an early-return guard over nesting; hoist a compound
  condition into a named local `bool` rather than packing three clauses into the `if`.
- `nullable enable`, warnings as errors, latest language version.
- **Comments are self-contained and present tense.** A comment says what the code does, the
  invariant a caller relies on, or *why this and not that*. It never says when it was written, what
  the code was before, or which planning document discussed it.
  - **No history or schedule.** Not `used to`, `previously`, `no longer`, `for now`. Describe the
    failure the code prevents as a consequence of not having the code, which is timeless, rather
    than as a past event, which is not.
  - **No issue or pull-request numbers** unless they name open work the reader has to tolerate.
  - **Measurements stay only when the code depends on the number.**
  - **Long "why" is welcome**, in the shape "X, because Y".
  - **Public types and members keep an XML summary.** A private member gets one when the reason it
    exists is not visible from its code.

Commit at every milestone boundary and whenever a self-contained piece works. Run `dotnet format`
first so formatting never shows up as diff noise.
