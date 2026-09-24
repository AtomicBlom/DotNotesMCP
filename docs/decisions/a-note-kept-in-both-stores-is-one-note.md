# A note kept in both stores is one note

**Decision.** `scope: both` on a write puts one note in both stores, joined by a shared `dn-id`.
Search and read show a pair as one hit that says it is in both. Both copies live on: nothing retires
the machine copy, because nothing can know that every branch this machine checks out has the
committed one. A committed note that has ever been paired is retired by superseding it rather than
deleting it, so the retirement reaches private copies that git cannot.

**Why a note belongs in both.** A fact true of every branch -- a compile error every checkout hits --
written to repository scope reaches the other worktrees only when the branch that learned it merges,
and a worktree cut from an older commit never sees it at all. The
[committed-store decision](the-committed-store-follows-the-checkout.md) answers that by writing the
note to machine scope as well. This makes it one call instead of two, and one hit instead of two.

**Why `both` is still a stated scope.** It is a third answer to the question the caller has to answer,
not a default for leaving it out. It publishes, so it carries everything repository scope carries.

**Why the pair is joined by an id rather than a name.** Renaming one copy must not split the pair, and
two unrelated notes may share a name across the stores. `dn-id` is server-owned, so it is already
outside the source hash.

**Why an update travels toward the machine and never away.** Writing the repository copy of a pair
also writes its machine copy, because making a fact private cannot publish anything. Writing the
machine copy leaves the committed one alone, because an update that reached the committed copy
without being asked to is a publication nobody chose -- the same asymmetry the no-default scope rule
rests on. The machine copy is followed only while it still matches the committed copy's previous
content. Once the person has edited it, it is theirs, and the divergence is reported rather than
overwritten.

**Which copy answers.** The committed copy, when this checkout has it, because it is the one that was
reviewed. The machine copy otherwise. `note_check` reports a pair whose copies differ.

**Why neither copy retires the other.** A committed copy missing from this checkout means either a
branch older than the note or a note deleted upstream, and nothing on disk tells the two apart without
walking history. Retiring on absence deletes the private copy at exactly the moment it is the only
one this worktree can see.

**Retiring a committed note.** `superseded`, an authored key holding the reason or the
`[[replacement]]`, keeps the file, drops the note from search, and makes a read say what replaced it.
It is authored rather than `dn-` prefixed because it is a judgement a person reads and can reverse. It
travels by git like any other edit, and a checkout that sees a superseded committed note carrying a
`dn-id` marks the machine copy superseded too, again toward the machine. `note_delete` on a committed
note carrying a `dn-id` supersedes it and says so, because the id means a private copy may exist on
some machine that only the file can reach. A committed note with no `dn-id` is deleted outright.
`note_check` lists the superseded notes, and deleting one for good is a person's call.

**What it costs.** Superseded notes stay in the repository until somebody deletes them, and a machine
that never checks out a branch carrying the supersession keeps a live private copy. Both are the
price of not guessing.

**What would change the answer.** Reading history cheaply enough to tell a deleted note from a branch
that predates it. Absence would then mean something, and a tombstone would no longer be needed.
