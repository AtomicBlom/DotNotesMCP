# A fact goes where it is read, not in the instructions

**Decision.** Anything a caller needs at the moment of a particular choice lives on the argument or
the description for that choice. `ServerInstructions` carries only what is about the server rather
than about one tool.

**Why.** The instructions are sent during initialize, so every session pays for them before making
a single call -- including the many sessions that read a note and never write one, and the many
more that do neither. A tool description is paid for the same way but read only once that tool is a
candidate. An argument's description is read at the instant the argument is being filled in.

The scope decision is the case that made this concrete. It was a paragraph of the instructions: what
`repository` means, what `machine` means, and that choosing wrong cannot be undone. All of it is
true and all of it matters, and none of it is useful until somebody is actually writing a note. Moved
to the `scope` argument, it costs nothing in a session that never writes, and it is in front of the
caller exactly when the choice is being made rather than several thousand tokens earlier.

That one move took the instructions from roughly 2,100 characters to roughly 800.

**What stayed.** Only what no per-tool description can carry, because it is about the server: that
notes are keyed to the repository so worktrees share them, that there is no setup call, and what is
worth writing down at all. The last of those has nowhere else to go -- the trigger for writing a
note fires when no tool is yet a candidate, so a description cannot reach it.

**What was cut outright.** "Every result names the store that answered." The field is *in* every
result; a model reading the result does not need to have been told it would be there.

**What it costs.** The same fact can now be needed in two places, and keeping them from drifting is
a judgement rather than a rule. The guard is the budget test on the total, which makes duplication
show up as a number going the wrong way.

**What changes the answer.** A client that fetches tool descriptions lazily and instructions eagerly
already makes this trade for you, and a client that does the opposite inverts it. Neither exists
today; the measurement to redo is which text a session actually pays for.
