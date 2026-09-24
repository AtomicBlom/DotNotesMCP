# What a note file may contain

Read before touching frontmatter keys, the splice, slugs, wikilinks or the index generator.

- **A write changes the keys it was given and returns every other byte unaltered.** The store is a
  vault a person edits, with their own properties, comments and ordering. Nothing round-trips
  through a serializer. See [the decision](../decisions/a-note-is-spliced-rather-than-rewritten.md).
- **Every key this server writes is either one of the authored fields, `dn-` prefixed, or `tags`.**
  `tags` is the one key it shares with the person, and it owns only the entries inside it that begin
  `dn/`. Everything else in that list comes back in the order it went in. It still never touches
  `aliases`.
- **A derived value in a shared key is excluded from the source hash.** `SourceHash` drops the
  `dn-` keys *and* the `dn/` entries of `tags`, and drops `tags` entirely when nothing but machine
  topics is left. Miss any of those and writing an enrichment stales the note it just enriched.
- **A value that YAML would read as another type is quoted.** A description of `no` coming back as
  `false` is the kind of wrong nothing downstream notices.
- **A fence that never closes is body, not metadata.** Reading it as an unterminated block takes a
  note's prose for properties and loses it at the next write.
- **A note that cannot be parsed is still a note.** Frontmatter errors are reported by `note_check`,
  never thrown: one broken block must not take out a search across a whole store, and the prose
  underneath it is still worth finding.
- **Links inside code are not links.** A note explaining the syntax quotes it, and counting the
  quotation invents dangling targets and backlinks between notes that never mention each other.
- **A rename rewrites every inbound link, and recomputes its prefix.** A move between stores changes
  what a link has to say: a private note linking to one that has just been committed must say
  `[[repo:name]]` or it resolves to nothing.
- **The generated index is how a committed store is found**, so its frontmatter and marker come
  first, inside the head `CommittedStores` reads. Move them further down and every store stops being
  found -- which reads as a repository that never opted in.
- **The index file is generated and byte-stable for stable input.** An index whose order wandered
  would be rewritten whenever an unrelated note changed, which on a synced store is a replication
  and a stored revision for nothing.
