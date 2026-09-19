# The indexing mode declares only its own tools

**Decision.** `--mode index` serves the five `note_index_*` tools and nothing else. The note tools
and the indexing tools are never served together, and there is no code path on which both are
registered.

**Why.** Rose's rule is that a declared tool which cannot run is worse than an absent one, because
the caller finds out at the call having already chosen the approach. This is the corollary, and it
costs more: **a declared tool that *can* run but should not.** It does not fail. It works, and the
failure is a session that spent its afternoon doing something nobody asked for.

`note_index_next` in an ordinary session is a loop of several hundred iterations that rewrites
frontmatter in every note it touches. An agent that sees it will eventually decide that indexing is
a reasonable thing to do in the middle of something else, and it will be right that the tool works.

**Why not hide it behind an argument.** Hiding does not help: the tool is still declared, still
costs listing bytes in every session, and is still there to be chosen. The listing *is* the surface.

**Why not a second binary.** One executable, two modes. The stores, the repository resolution, the
frontmatter splice and the crawler are shared, and two binaries is two chances to disagree about a
file format.

**Why a separate registration method rather than a flag.** `AddDotNotes` and `AddDotNotesIndexing`
are different methods with no shared path. A flag would leave a value that could be wrong;
separate methods mean no code exists that could register both, which is cheaper to keep true than a
rule somebody has to remember.

**And no standing registration at all.** An indexing run is a batch job, so it is a command:
`tools/index-notes.ps1` passes the server with `--mcp-config`, so the tools exist for those
processes and nowhere else. A per-project registration is available for iterating on the enrichment
itself, but it is opt-in, uncommitted, and one line to remove.

**A run names one store, and the parser refuses otherwise.** Indexing rewrites every note it
touches, and the two stores are a repository working tree and, quite possibly, a whole Obsidian
vault. A run that covered both because nobody said which is how somebody meaning to enrich a dozen
committed notes rewrites nine hundred personal ones. `--scope` is required, and a tool call naming
a different store than the run was started against is refused rather than honoured.

**What changes the answer.** A client that can show a tool group only when asked for it, so the
cost and the temptation both disappear. Until then the only reliable way not to offer a tool is not
to declare it.
