# Writing into a folder somebody else is also editing

Read before touching any write path, the store lock, or anything that runs during a write.

The store may be an Obsidian vault on a synced drive, open in an editor, on a machine that is one of
two. Every rule here is about that.

- **A write is whole-file, to a temporary name in the same directory, then moved over.** A sync
  service replicating a file mid-write is what this prevents, and the temporary name is unique per
  call rather than per process.
- **A write whose content is unchanged does not happen.** On a synced store a no-op write is a
  replication and a stored revision, so re-indexing an unchanged corpus has to cost nothing.
- **Replacing an existing note requires the revision a read reported.** These are files a person
  edits while a session is running; an unconditional write is a way to lose an edit made thirty
  seconds ago and never learn of it. Creating a note needs no revision -- there is nothing to lose.
- **The lock is an open handle, and it lives outside the store.** The OS releases it however the
  holder dies, so there is never a stale one; and a lock file inside a synced vault would reach the
  other machine minutes late, where it is indistinguishable from a live one. See
  [the decision](../decisions/the-store-lock-is-an-os-handle-outside-the-synced-store.md).
- **Cross-machine safety never depends on timing.** It is whole-file writes, content hashes, and
  `note_check` reporting the conflict copies a sync service leaves behind.
- **A hash is taken over content with line endings normalised.** The same note is CRLF in a checkout
  under this repository's own `eol=crlf` attribute and LF wherever git stored it; hashing the bytes
  would make a revision depend on which machine checked it out, and every clone would see every note
  as modified.
