# Notes are keyed to the repository, not the checkout

**Decision.** The repository a directory belongs to -- not the directory itself -- is what names it,
and `RepositoryIdentity.For` is the one place that answers. Six worktrees of one repository share one
name, one key and one machine store. Nothing else may key on a path.

**Scope.** This is about identity, and so about the machine store. Where a *committed* note goes is
[a separate decision](the-committed-store-follows-the-checkout.md): that store is tracked, git
reconciles it already, and it follows the checkout the call came from.

**Why.** This is the defect the server exists to remove, so it is worth stating as the thing itself
rather than as a feature. Claude Code stores memory under a path-encoded working directory, and on
this machine that has produced six stores for Loom and eight for RoseMCP, four of them empty. None
of them can see the others. The seventh worktree starts with nothing, which is the moment the
memory is least useful and most expected to work: a fresh branch is exactly when the accumulated
"this repository does X" is worth having.

**And the worktree that is deleted takes its notes with it.** This is the sharper half. A stale store
beside five others is merely wasteful; a store whose path no longer exists is unreachable, because
nothing will ever resolve to that key again and there is no history to recover it from. A worktree is
a thing people throw away on purpose. What was learned in it should not be thrown away with it.

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

**What it costs.** Identity is resolved on every call and nothing about it is remembered between
them, because the two things it reads are exactly the two a person changes while a session is open.
A resolution is three to five `File.Exists` probes and at most five small reads: 190 microseconds
inside a repository, 291 outside one, against a store crawl of tens of milliseconds.

**What changes the answer.** Nothing about worktrees. If a future git makes `commondir` optional for
linked worktrees, the submodule and worktree cases stop being separable by that file alone and the
`worktrees/` versus `modules/` path segment becomes the discriminator instead.
