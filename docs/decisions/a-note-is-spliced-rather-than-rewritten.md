# A note is spliced, not rewritten

**Decision.** Frontmatter is read with a YAML parser and written by replacing named keys' lines as
text. A write changes the keys it was given and returns every other byte unaltered. Nothing
round-trips a note through a serializer.

**Why not round-trip.** A serializer emits its own idea of the document: its quoting, its key order,
its indentation, its flow-versus-block choice, and no comments at all, because comments are not part
of the data model. The store is a vault a person edits in Obsidian, with their own properties, their
own ordering and their own notes-to-self in `#` comments. Round-tripping would destroy all of that
on the first write -- and then a re-index of a corpus nobody had touched would be a diff on every
file, which on a synced drive is a full replication and a stored revision per note.

**Why parse with YAML anyway.** Reading is the part that has to understand what a person typed:
`'one'` and `one` and `"one"`, a folded scalar over three lines, a flow sequence, a block sequence.
Hand-parsing that is how you eventually read a value wrong and write a wrong one back. So the
parser is authoritative for *values* and the text is authoritative for *bytes*, and each does the
job it is good at.

**How the line range is found.** A top-level entry starts at a line whose first character is not
whitespace and which carries a colon; it owns every following line that is indented or blank. That
is what carries a block sequence, a folded scalar and a wrapped value along with the key they belong
to. A line at column zero with no colon -- a document somebody half-edited -- is left exactly where
it is rather than being attached to the key above it.

**Keys are replaced in place.** Obsidian shows properties in file order, so a machine-written key
that shuffled to the end on every write would make each re-index a visible change to a file nobody
edited. Only a genuinely new key is appended.

**What it costs.** Two representations of one thing, which have to agree about what a key is. The
test that holds them together is the round-trip one: anything spliced in must parse back as the
value that went in, including values YAML would otherwise read as a boolean, a number or a date.

**What changes the answer.** A frontmatter schema complex enough that keys nest. Then "the lines a
key owns" stops being a line range and the splice needs a real editing model -- at which point a
round-trip with comment preservation is the smaller thing to build.
