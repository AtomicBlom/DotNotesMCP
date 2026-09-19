# Notes are keyed to the repository, not the checkout

**Decision.** Every store path derives from `RepositoryIdentity.For`, which answers with the
repository a directory belongs to rather than the directory itself. Six worktrees of one repository
share one set of notes. Nothing else may key on a path.

**Why.** This is the defect the server exists to remove, so it is worth stating as the thing itself
rather than as a feature. Claude Code stores memory under a path-encoded working directory, and on
this machine that has produced six stores for Loom and eight for RoseMCP, four of them empty. None
of them can see the others. The seventh worktree starts with nothing, which is the moment the
memory is least useful and most expected to work: a fresh branch is exactly when the accumulated
"this repository does X" is worth having.

**What the key is derived from.** `git rev-parse --git-common-dir`, reached by reading `commondir`
rather than by running git. A linked worktree's `.git` is a file naming a directory under the main
repository's `worktrees` folder, and that directory carries a `commondir` pointing back. Following
it is one file read and it collapses every worktree onto the main checkout.

**Why a submodule is not folded in.** A submodule reaches its git directory through a `.git` file
too, and looks identical until you notice it has no `commondir`. It has its own remote, its own
history and its own contributors, so its notes are its own. One absent file separates the two cases,
and treating them alike would file a vendored library's notes under the superproject that happens to
contain it.

**Why paths are folded before they are keyed.** A drive letter is case-insensitive on Windows and a
path-encoded directory name is not, which is why `D--Contoso-Platform` and `d--Contoso-Platform` are two
stores for one repository in the memory this replaces. Every path passes `PathCasing.Fold` before it
is compared or hashed.

**What it costs.** Identity is resolved on every call, so it is cached per process on the folded
start directory. A repository does not move while a process is running. A cold resolution is three
to five `File.Exists` probes and at most four small reads.

**What changes the answer.** Nothing about worktrees. If a future git makes `commondir` optional for
linked worktrees, the submodule and worktree cases stop being separable by that file alone and the
`worktrees/` versus `modules/` path segment becomes the discriminator instead.
