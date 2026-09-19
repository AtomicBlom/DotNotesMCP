# The store lock is an OS handle, kept outside the store

**Decision.** One writer per store, held as a file opened `FileShare.None` at
`%LOCALAPPDATA%/BinaryVibrance/DotNotes/locks/<hash>.lock`. Never inside the store.

**Why a handle rather than a lock record.** A file whose *contents* say who holds it has to be
expired by whoever finds it, which means deciding whether the holder is dead or merely slow.
Deciding wrong in one direction corrupts a store, and in the other wedges it until somebody deletes
a file by hand. An operating system handle is released by the operating system however the holder
dies -- crash, kill, power loss -- so there is never a stale lock and nothing has to tidy up after
being killed. `DeleteOnClose` keeps the directory from accumulating one file per store ever opened.

**Why outside the store.** A store may be an Obsidian vault on a synced drive. A lock file there is
just another file to replicate: it reaches the other machine minutes later, where it is
indistinguishable from a live one, and it rewrites itself often enough to generate sync traffic and
revisions for nothing. Keeping it in local application data makes this an honest same-machine
mutex, and the drive never sees it.

**What it does and does not cover.** It covers the contention that actually happens: two Claude Code
sessions on one box, or several indexing processes. It does not cover two machines, and no lock
can -- the lock would itself have to be synced. Cross-machine safety comes from things that do not
depend on timing: whole-file writes to a temp name then a move, writes skipped when the content is
unchanged, content hashes on every read, and `note_check` reporting the conflict copies a sync
service leaves behind.

**Why it waits rather than failing.** The thing being waited for is one whole-file write, over in
milliseconds. A caller that gave up immediately would surface contention between two of the
person's own sessions as a refusal they can do nothing about.

**Why it is keyed on the folded path.** Two spellings of one store taking two locks means both
writers proceed and one write is lost. Same folding, same reason, as the drive-letter case that
splits a repository's notes.

**What changes the answer.** A store on a filesystem where `FileShare.None` is advisory rather than
enforced -- an NFS mount, some container layers. Then the guarantee is gone and the honest response
is to refuse that store rather than to pretend, because a mutex that looks like it works is worse
than none.
