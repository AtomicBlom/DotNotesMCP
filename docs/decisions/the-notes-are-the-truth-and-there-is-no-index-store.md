# The notes are the truth, and there is no index store

**Decision.** The search index is a dictionary in memory, built by crawling the store on first use
and rebuilt when the store changes. Nothing is persisted. No SQLite, no LiteDB, no embedded engine.

**Why.** A few hundred markdown files is not a database problem. The measured shape of a real store
is 63 notes and 394 KB; crawling and tokenising that is tens of milliseconds. Against that, a
persisted index brings a schema to migrate, a file that can disagree with the notes, a question
about where it lives so a sync service does not corrupt it, and -- for SQLite -- native assets per
architecture, on a pair of machines that are x64 and ARM64.

What it costs instead is a tokenizer, a postings map and a BM25 scorer: around three hundred lines,
deterministic, unit-testable with no I/O.

**Why not LiteDB specifically.** It has no full-text search at all: no BM25, no tokenizer, no
snippet. Choosing it means taking a dependency *and* writing the ranking. It is dominated in both
directions -- by SQLite if a query engine is wanted, and by writing it if one is not.

**And the tokenizer had to be written anyway**, which is what settles it. See
[the tokenizer decision](the-tokenizer-splits-identifiers-and-keeps-them-whole.md): the one
retrieval behaviour this corpus most needs is the one no off-the-shelf index provides without
native code. Once the hard part is ours, the storage engine buys only storage, for a few hundred
kilobytes that rebuild in milliseconds.

**Freshness comes free.** A change a person makes in Obsidian is visible to the next search, because
the next search checks whether the store has changed -- a file count and the newest write time,
cheap enough to ask every call. There is no cache to invalidate across a restart and no reconcile
step at startup, because there is nothing that outlives the process.

**Staleness cannot happen.** The index and the notes cannot disagree, because the index is not
written down. That removes an entire class of bug rather than testing for it.

**What changes the answer.** A crawl the caller can feel. The threshold is the stability rule's:
roughly 300 ms of cold start, which is where a first search stops feeling instant and an agent
starts reaching for the files instead. `CrawlCostTests` records the number so the decision is made
against a measurement. When it arrives, the fix is to serialize the postings map that already
exists and invalidate it by note hash -- a file and about a hundred lines -- not to adopt a
database. `INoteSearch` is the seam, and `NoteSearchResult.Backend` names which implementation
answered so the change is visible rather than assumed.
