# A renamed repository keeps its notes until the move is made

**Decision.** The key still comes from the naming chain, fresh on every call. Beside it, a
machine-local evidence file records what each repository looked like when it was seen: its common
directory, its remote, its root commits, the key it resolved to, and the committed stores found in
it. When that evidence attributes another machine store to the repository a call resolved to, the
store is read alongside this one and every result carries a notice naming the pending move. The move
itself is an explicit step. Nothing renames a store on its own.

**Why a key changes at all.** The chain answers config, then remote, then folder, and each step is
right about the repository as it is at that moment. Adding an origin moves a repository from
`name-<pathhash>` to `name`. Moving a repository with no remote changes the hash. Renaming it on the
host changes the remote. Each is a person doing something ordinary, and each strands the old store
without a word, because a key with no store reads exactly like a repository nobody has written notes
for.

**Why the root commits are evidence rather than the key.**

- There is none before the first commit, which is the moment a repository is being set up -- so the
  key would still change, at the first commit instead of at `git remote add`.
- Finding them from disk means reading `commit-graph`, or walking packs where there is none, and the
  key is needed on every call.
- They are not unique and not always there. A shallow clone lacks them, a fork shares them with its
  upstream, and rewriting history replaces them.
- A hash is a folder name nobody can read in a vault.

As evidence, each of those weaknesses costs a missed match, and a missed match is no worse than
having no evidence.

**Why the root commits are a set.** A monorepo assembled from three repositories has three roots, and
each original's store recorded one of them. Matching is by intersection: merging unrelated history
adds a root and never removes the one an older store recorded.

**Why nothing is renamed automatically.** Several worktrees of one repository are live at once, each
with its own server process, and the evidence can be wrong in ways only a person can see. A fork's
fresh clone shares its roots with upstream. A candidate whose checkout still exists may be another
repository's live store. A move the person chose is one they know about; a move that happened to
them is a folder that vanished from their vault. Reading the evidenced store costs nothing when the
evidence is wrong beyond a hit attributed plainly to the store it came from, so the evidence is used
to read, and the move waits for someone to make it.

**Where a write goes while a move is pending.** To the resolved key's store if it exists. Otherwise
to the evidenced store, when there is exactly one and nothing else can own it -- every checkout it was
seen with, other than this one, is gone -- so that the pending move stays
a rename rather than becoming a merge. Otherwise to the resolved key's store, created as it would be
anyway. A fork's upstream fails that test while its checkout is still here, which is what keeps a
fork's notes out of its upstream's store. A machine write to a name that is already a note in a store
waiting to move is refused, because it would leave one note in two places.

**Why the evidence is consulted on every call, not only when a store is missing.** A lookup that runs
only on a miss stops running the moment anything writes to the new key, and the old store drops out
of every answer with no notice at all. A pending move lasts until it is made or dismissed.

**The explicit step.** `DotNotes.Server --adopt <dir>` moves the evidenced stores to the resolved
key: a rename when there is one and the key has no store yet, a merge otherwise. A merge plans every
file before it moves one, drops a file already there with the same bytes, and refuses -- moving
nothing -- when two files claim one name with different contents. `--dismiss <dir>` records that the
candidates are not this repository's, which is the fork's answer, and `--only <folder>` narrows
either to one candidate. Both take the store lock of every store they touch, in one order. A command
rather than a tool, because it runs once per rename and a tool's description is paid for by every
session; the notice names the command with this process's own path, and an agent can run it or
pass it on.

**The same path serves a change of keying.** A remote-named repository is keyed by its remote's
whole path -- `atomicblom-rosemcp` -- because the last segment alone let `a/tools` and `b/tools` share
a store. Every store under a short name then belongs to a key that no longer names it, and nothing
was recorded about it. So the folder the short name would be is offered as a candidate without
evidence, and is written to only while no other existing checkout has been seen using it: the first
repository to use it recognises it, and the second, if two shared one name, reads it and is told.

**Where the evidence lives.** `%LOCALAPPDATA%\BinaryVibrance\DotNotes\repositories.json`, beside the
settings and the locks. Not in the repository, because it describes this machine. Not in the machine
store, because a vault may sync, and another machine's paths are noise there. It is written like
every other file here: whole, to a temporary name, moved over, under a lock, and not at all when
nothing changed.

**Why this is not the cache that was removed.** That cache trusted a remembered answer to the two
things a person changes while a session is open. This file answers neither: the key and the opt-in
are resolved fresh, and every path it remembers is checked on disk before it is used. Deleting it
loses the ability to notice a move, and nothing else.

**What it costs.** One small file read on every call. A root-commit scan of `commit-graph` whenever
the graph changes, which is when git collects; the first line of the `HEAD` reflog covers a
repository made with `git init` that has not been collected yet, and a clone with neither
contributes no root evidence. A parentless commit that `git stash -u` makes counts as a root, which
is harmless: it is still this repository's. And a notice on every result until the move is made,
which is the point.

**What would change the answer.** A client that cannot run a command makes `adopt` a tool argument
instead. Evidence that proves right often enough that nobody ever dismisses a candidate would let a
single unambiguous one move by itself.
