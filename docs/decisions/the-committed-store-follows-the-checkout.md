# The committed store follows the checkout

**Decision.** The machine store is keyed to the repository. The repository store is the
`.dotnotes/notes/` of the working tree the call came from, so a note written in a linked worktree is
committed on that worktree's branch rather than into the main checkout.

**Why the two stores split here.** They fail in opposite directions, and one rule cannot serve both.

The machine store is untracked and nothing reconciles it. Keyed to the checkout, a note is stranded
the moment the worktree is deleted: the path stops matching anything, so nothing will ever resolve
to it again and there is no history to recover it from. That is the defect this server exists to
remove -- see
[notes are keyed to the repository](notes-are-keyed-to-the-repository-not-the-checkout.md) -- and it
is why that store outlives every checkout.

The repository store is tracked, and git already reconciles it. A note committed on a branch reaches
the other worktrees the way every other tracked file does, and the merge is the reconciliation.
Keying it to the repository buys a sharing git already provides, and pays for it with a note that
lands on whichever branch the main checkout happens to have out, in a working tree nobody in the
session is looking at, where `git add` in the worktree never finds it and the review of the change
it describes never sees it.

**Why the gate is read from the checkout as well.** `.dotnotes/dotnotes.json` is a committed file, so
opting in is a commit, and a commit happens on a branch. Gating on the main checkout means creating
the file the refusal just named does nothing until it merges -- the same failure the identity cache
was removed to avoid, and the worse half of it, because the person has no reason left to doubt they
did it right.

**Why the name in it is not.** The name decides the machine store's key, and a key that varies by
branch is one repository with two private stores. So `RepositoryIdentity.NamingConfig` reads the main
checkout's copy and only that one may name; the checkout's own copy gates and locates its notes.
Everywhere but a linked worktree they are the same file, and the second read is skipped.

**What the alternative was.** Routing both stores through the main checkout, which is what the keying
rule said while storing and keying were one rule. It gives one property the split does not: a note
written in one live worktree is visible in another before it merges. That case is real and rarer
than it looks, and its answer is to write the note to machine scope as well -- a call made
deliberately, rather than a branch mixing paid on every write.

**What it costs.** A worktree cut from an older commit sees that commit's committed notes rather than
the newest, and a note written on a branch is invisible to the others until it merges. That is how
every tracked file behaves, so it surprises nobody who has noticed it is a tracked file.

**What would change the answer.** A committed store that is not per-branch -- notes on an orphan
branch, or in a git notes ref. There would then be one tracked store with no branch to belong to,
and the checkout would stop being the right thing to follow.
