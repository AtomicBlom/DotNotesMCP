# Where a note goes

Read before touching `NoteStores`, a scope, the machine-store path, or the configuration precedence.

- **Scope is stated on a write, never defaulted.** It is the one decision here with no undo: a
  private note can be promoted, and a pushed one cannot be recalled. The explanation lives on the
  argument rather than in the instructions, because that is where it is read at the moment the
  choice is being made.
- **Repository scope is available only where the repository opted in**, by committing
  `.dotnotes/dotnotes.json`. Without the gate, a server registered once and used everywhere drops an
  untracked folder into whichever repository happened to be open, on an agent's initiative.
- **The default machine store is created on demand; a store somebody chose is not.** A configured
  path that cannot be reached refuses and names itself. Falling back would write notes to a second
  place nobody is looking at, which is the fragmentation this server removes arriving by a different
  door.
- **A missing config file means defaults; a malformed one refuses.** These two files decide where
  data goes rather than how something behaves, and defaulting past a typo relocates a repository's
  notes silently. See
  [the decision](../decisions/a-malformed-config-file-refuses-rather-than-defaulting.md).
- **Nothing per-machine overrides the repository store's path.** A repository store at a path that
  differs per machine is not a repository store, and two clones would quietly stop sharing one.
- **Resolving never creates a directory.** Asking where the stores are is what a diagnostic does,
  and a diagnostic that leaves a folder behind in every repository it is pointed at is a worse
  diagnostic.
- **Every `DOTNOTES_*` variable is read in exactly one place, through `NoteOptions.Environment`.**
  The process environment is shared by everything in the process, including a parallel test suite.
  See [the decision](../decisions/the-environment-is-read-through-a-seam.md).
