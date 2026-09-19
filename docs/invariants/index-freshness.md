# When enrichment is stale

Read before touching a claim, a lease, the stamp a note carries, or anything in `--mode index`.

- **The hash a note is judged by excludes the keys the indexer writes.** Everything about this
  design conspires against a loop that terminates, because the enrichment is written into the note
  it describes. Hash the whole file and every note is stale the instant it is finished; the loop
  runs forever and nothing looks wrong until the bill arrives.
- **There is no watermark.** Notes have no natural order, and a hand-edit mid-run breaks any
  ordering a watermark relies on -- silently, by skipping whatever moved behind it. Freshness is a
  property of each note, so what is left is a scan and resuming is running it again.
- **A note carries its own schema, prompt and source stamp.** That is what lets a machine which has
  never indexed this store tell fresh from stale, so a second machine or a fresh clone does not
  re-enrich a corpus somebody already paid for.
- **A crash between writing the note and updating the journal needs no recovery.** The next crawl
  adopts what the note already says, which is the direct payoff of the notes being the truth.
- **An expired lease is still honoured while nothing else has touched the note.** Twelve minutes on
  a long note is correct work, and discarding it costs a turn and buys nothing. What is refused is a
  write over a note somebody else has since enriched or edited.
- **A note that keeps failing is eventually left alone.** A loop that cannot finish is worse than a
  corpus with three unindexed notes in it.
- **Attempts and skips are keyed by the content they were about.** Editing a note gives it a fresh
  start rather than inheriting a verdict passed on different text.
- **A rebuild marks notes without rewriting them.** Stripping each note's enrichment to force it
  stale would be a full sync for a decision the next run may reverse.
- **The indexing tools are never served beside the note tools.** They work, which is the problem.
  See [the decision](../decisions/the-indexing-mode-declares-only-its-own-tools.md).
