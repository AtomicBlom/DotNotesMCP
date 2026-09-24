# Where a note goes

Read before touching `NoteStores`, a scope, the machine-store path, or the configuration precedence.

- **Scope is stated on a write, never defaulted.** It is the one decision here with no undo: a
  private note can be promoted, and a pushed one cannot be recalled. The explanation lives on the
  argument rather than in the instructions, because that is where it is read at the moment the
  choice is being made.
- **The machine store is keyed to the repository; the repository store is the checkout's own.** The
  two fail in opposite directions, so they do not share a rule. An untracked store keyed to a
  checkout is stranded when that checkout is deleted, and nothing will resolve to it again. A
  tracked store keyed to the repository lands notes on whichever branch the main checkout has out,
  in a working tree nobody in the session is looking at. See
  [the decision](../decisions/the-committed-store-follows-the-checkout.md).
- **Repository scope is available only where the checkout opted in**: it has a committed store --
  a folder holding a generated index, wherever it has been moved -- or a `.dotnotes/dotnotes.json`.
  Without the gate, a server registered once and used everywhere drops an untracked folder into
  whichever repository happened to be open, on an agent's initiative. The gate is read from the
  checkout the call came from, because opting in is a commit and a commit happens on a branch --
  gating on the main checkout makes running the command the refusal just named do nothing until it
  merges. See [the decision](../decisions/the-committed-store-is-found-rather-than-configured.md).
- **A found store beats a configured folder with nothing in it.** That is a store somebody moved
  without editing the config; writing where the config still points starts a second store beside
  the real one. With several stores, the nearest enclosing the call's directory wins, and a write
  with nothing to choose between them refuses rather than picking one.
- **Only the main checkout's config may name the repository.** The gate and the notes path come from
  the checkout that asked; the name does not. A name that varies by branch is one repository with two
  machine stores, which is the fragmentation this server removes arriving by a different door.
  `RepositoryIdentity.NamingConfig` is the one that names, and it is the same file as `Config`
  everywhere but a linked worktree.
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
- **A pending move never moves anything by itself.** While the evidence attributes another machine
  store to this repository, that store is read with this one and every answer says so. Writes go to
  the key's store, or -- only where the key has no store yet and there is exactly one candidate that
  nothing else can own -- to that candidate, so the move stays a rename. Several worktrees are live
  at once and the evidence can be wrong; a store is moved only by `--adopt`. See
  [the decision](../decisions/a-renamed-repository-keeps-its-notes-until-the-move-is-made.md).
- **A store read alongside is never written or enriched.** It is not under the lock the write takes,
  and an index regenerated over it would list links into a folder beside the store. Anything that
  writes filters to `NoteStore.Holds`.
- **Every `DOTNOTES_*` variable is read in exactly one place, through `NoteOptions.Environment`.**
  The process environment is shared by everything in the process, including a parallel test suite.
  See [the decision](../decisions/the-environment-is-read-through-a-seam.md).
