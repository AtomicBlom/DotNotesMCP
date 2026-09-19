# The index is generated, and frontmatter is flat

Two decisions about what a person sees, which share a reason: the store is read by a human in
Obsidian, and anything that only an agent can use is half a store.

## The index is generated

**Decision.** Every folder of notes carries an `index.md` regenerated from the frontmatter of the
notes beside it. It opens with a marker that Obsidian hides in reading view, carries
`dotnotes-generated: true`, and is overwritten without asking.

**Why.** The memory this replaces keeps a hand-written `MEMORY.md`, and the real one on this machine
has drifted exactly as a hand-written index does: lines for notes that were deleted, notes with no
line, and a 41 KB document compressed into a clause somebody wrote once and never revisited. Nothing
reconciles it because nothing can -- a person writing prose is not a process. Generating it means
the index and the notes cannot disagree.

**Why it is byte-stable.** Same notes in, same bytes out, ordered by name. An index whose order
wandered would be rewritten whenever an unrelated note changed, and on a synced store every rewrite
is a replication and a stored revision. Ordering by name rather than by date is what makes editing
a note not reorder the index.

**Why it says it is generated.** A person who opens it should see at once that editing it is
pointless. The marker is also what keeps the index out of the crawl: a listing of every note would
otherwise match every search.

## Frontmatter is flat

**Decision.** Every key this server writes sits at the top level. Nothing nests.

**Why.** Obsidian's properties pane and the Bases core plugin -- both enabled, and the only query
surface available in a vault with no community plugins -- read top-level keys. A nested map renders
as an opaque blob in the pane and is invisible to Bases. Flat keys are editable by hand, filterable
in a Bases view, and show up in the tag and property panes.

**What was dropped from the dialect this borrows from.** `node_type`, which has one value and so
says nothing; `originSessionId`, which names nothing a reader can open, and is the memory-file
equivalent of the closed-issue number this repository's comment rules already forbid; and
millisecond timestamps, because a byte change on every touch is a sync and a revision on every
touch.

**The nested dialect is still read.** A note copied into a store by hand comes from a store that
writes `type` under `metadata`, and its author's type should survive rather than silently becoming a
project note across a whole corpus. Read, never written.

## Topics are nested tags, not a key of their own

**Decision.** The indexing mode writes its topics into Obsidian's own `tags`, prefixed `dn/`. It
owns only the prefixed entries; everything else in that list is the person's and comes back
untouched.

**Why this overrides "never write a key you do not own".** That rule is in service of the person,
and here it was working against them. A topic under a key of our own is invisible to Obsidian: no
tag pane, no node in the graph, nothing for a Bases view to filter on. Since the entire reason to
point the machine store at a vault is that the person gets something from it, a topic they cannot
see is a topic that does not exist for them.

**Why nested.** `dn/analyzers` groups every machine topic under one collapsible parent, so forty of
them do not bury the handful the person wrote. The prefix is also what makes the merge tractable: a
write replaces the prefixed entries and returns the rest in order, which is deterministic and so
rewrites nothing when a re-index changes nothing.

**A topic the author already tagged is not added again.** Two entries for one word is two nodes in
the graph and two rows in the pane, for a distinction the reader does not care about. The prefix
marks what the indexer *added*, not what it merely agreed with -- and the author's plain tags count
as topics for the vocabulary, so the indexer does not declare one as new when the person has already
used it.

**What it cost.** The source hash had to learn about entries rather than only keys. Writing topics
into a shared key means the hash that decides staleness now excludes the `dn/` entries of `tags`,
and excludes `tags` entirely where nothing else is in it. Getting that wrong stales every note the
moment it is enriched and the loop never ends; the test named for it is what caught it.

**What changes the answer.** Obsidian growing a property type that the graph and the tag pane read
without it being `tags`. Then the topics move there and the shared key goes back to being the
person's alone.

**What changes the answer.** Obsidian supporting nested properties in the pane and in Bases. Then
grouping the server's own keys under one `dn` map would be tidier than a prefix, and the prefix
could go.
