# The tokenizer splits identifiers and keeps them whole

**Decision.** Every run of letters, digits and joiners is indexed as itself *and* as the words
inside it. `GeneratedBindableCustomProperty` yields that token plus `generated`, `bindable`,
`custom`, `property`. `Db.Primary` yields `db.primary`, `db`, `primary`. `arm64` yields `arm64`,
`arm`, `64`.

**Why this is the reason the index is written rather than taken.** The terms that tell these notes
apart are project-specific identifiers -- `GeneratedBindableCustomProperty`,
`IBindableCustomPropertyImplementation`, `Db.Primary`, `TenantId` -- and the person searching
six weeks later types the words, not the identifier. A tokenizer that keeps identifiers whole never
finds the note. One that only splits them stops matching the exact name, which is the strongest
signal there is. Emitting both costs a longer posting list, which at this corpus size is nothing,
and finds the note either way.

No off-the-shelf index does this. SQLite's FTS5 offers `tokenchars` to keep `Db.Primary` together
and nothing at all for case boundaries; a custom tokenizer there means native code. So the choice
was never "write a tokenizer or use a library" -- it was "write a tokenizer, or give up the one
retrieval behaviour this corpus most needs".

**The acronym rule.** A run of capitals ends where the next word begins, so `XMLHttpRequest` breaks
before the `H` and not before the `T`. Without it the segment yields `xmlhttp`, and nobody's query
says that.

**Case marks the boundaries and does not survive them.** Every term comes out lower-cased, so a
query matches whatever case it was typed in. But `MSBuildLocator` and `msbuildlocator` genuinely do
not produce the same terms -- the first can be segmented and the second cannot -- and pretending
otherwise would mean either losing the segments or inventing them.

**What it costs.** Roughly three terms per word rather than one, so the index and every posting list
are that much bigger. At a few hundred notes this is a few megabytes of dictionary, built in
milliseconds.

**What changes the answer.** A corpus that stops being technical. If the store fills with prose, the
identifiers stop being the discriminating terms, the extra postings stop paying for themselves, and
what matters becomes the paraphrase gap -- which is what the enrichment mode and, after that,
embeddings are for.
