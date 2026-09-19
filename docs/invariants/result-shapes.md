# What a result may say

Read before adding a tool, adding a field to a result, or changing an error path.

- **An error says what went wrong, not that something did.** The SDK replaces the message of any
  exception it does not recognise with "An error occurred invoking 'note_search'." A call-tool
  filter forwards the real message instead. Convert at the boundary, never at the throw site: the
  exception type carries meaning further in, and a refusal a caller cannot act on is
  indistinguishable from the tool being broken.
- **A store that refused says so on every answer it could not contribute to.** An empty result reads
  as "nothing to find", and a caller who believes that stops asking. This is the same rule as the
  refusals themselves, applied to the half of an answer that did work.
- **An empty store says it is empty.** `searched: 0` is the honest field, but it is one a caller has
  to interpret; the notice names it and names the tool that fixes it. Found by using the server
  rather than by testing it, which is the dogfooding rule earning its place.
- **Every result names the repository and the scope that answered**, filled once in `NoteService`
  rather than by each tool. A search that found nothing in the wrong store is shaped exactly like
  one that found nothing in the right store, and that is what six per-worktree stores feel like from
  the inside.
- **No tool returns a bare collection.** MCP gives structured content one JSON object, so a list has
  nowhere to go; every list is a named property of a record.
- **A hit carries an extract, never the note.** Ten notes returned whole is most of a working
  context spent on nine the caller will discard, and that cost is what stops a search being worth
  making on a hunch.
- **A discriminator rather than a null where the caller must notice.** `NoteAssignment` carries a
  required state, because a caller can miss a null note and cannot miss a field it has to read.
- **Read-only and destructive are stated per tool and pinned by a test.** Read-only is a promise a
  client may act on by not asking; destructive spends a confirmation. Marking the commonest write
  destructive teaches the user to click through the ones that matter.
