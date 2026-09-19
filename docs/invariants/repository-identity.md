# Which repository a call is about

Read before touching `RepositoryIdentity`, `GitLayout`, `RemoteName`, `PathCasing`, or anything that
turns a path into a key.

Every rule below is a way of getting a confidently wrong answer rather than a failure. A call that
resolves to the wrong key does not report an error: it reports no notes, which reads exactly like a
repository nobody has written notes for.

- **A key is derived from the repository, never from the directory a call came from.** A linked
  worktree's `.git` is a file naming a directory under the main repository's `worktrees` folder, and
  following the `commondir` inside it is what collapses every worktree onto one key. Skip that step
  and a repository has as many stores as it has checkouts -- six for one of them in the memory this
  replaces, with the seventh starting empty.
- **A submodule is its own repository.** It reaches its git directory through a `.git` file exactly
  as a worktree does, and the only difference is that it has no `commondir`. Treating the two alike
  files a vendored library's notes under whichever superproject happens to contain it.
- **Every path is folded before it is compared or hashed.** A drive letter is case-insensitive on
  Windows and a path-encoded directory name is not, which is why `D--Contoso-Platform` and
  `d--Contoso-Platform` are two stores for one repository in the memory this replaces.
- **A name from a folder carries a hash of its path; a name from a config file or a remote does
  not.** A folder name means something only on this machine, so two unrelated directories called
  `tools` must not share a store. A remote means the same thing everywhere, so two clones of one
  repository must.
- **Nothing runs `git`.** Every call needs an identity, a process launch costs more than the reads
  it replaces, `git` need not be on the PATH of a server an editor started, and a child process
  would move the most important table in this repository out of the fast suite.
- **Provenance is on the answer.** `NamedBy` says which step of the chain named the repository,
  because a name that surprises its reader is otherwise reverse-engineered from the filesystem.
