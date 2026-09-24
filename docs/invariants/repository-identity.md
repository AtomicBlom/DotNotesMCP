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
  replaces, with the seventh starting empty and a deleted one stranded for good, because no path
  will ever resolve to its key again.
- **This file is about identity, which is not the same question as location.** The key it produces
  says where the *machine* store is. A committed note goes to the working tree the call came from --
  `Worktree`, not `Root` -- because git already reconciles a tracked store. Read
  [store-routing.md](store-routing.md) before changing where anything is written.
- **A submodule is its own repository.** It reaches its git directory through a `.git` file exactly
  as a worktree does, and the only difference is that it has no `commondir`. Treating the two alike
  files a vendored library's notes under whichever superproject happens to contain it.
- **Every path is folded before it is compared or hashed.** A drive letter is case-insensitive on
  Windows and a path-encoded directory name is not, which is why `D--Contoso-Platform` and
  `d--Contoso-Platform` are two stores for one repository in the memory this replaces.
- **A name from a folder carries a hash of its path; a name from a remote is keyed by the remote's
  whole path; a configured name is the key as written.** A folder name means something only on this
  machine, so two unrelated directories called `tools` must not share a store. A remote means the
  same thing everywhere, so two clones of one repository must -- but its last segment does not, so
  `a/tools` and `b/tools` key as `a-tools` and `b-tools` while both are *named* `tools`. The host is
  left out of the key, because a person reads that folder name in their vault.
- **A store under the short name is offered as a pending move.** A remote-named repository's notes
  are under its last segment wherever a server keyed on it, with nothing recorded about that store,
  so `rosemcp` is a candidate for `atomicblom-rosemcp` without evidence. It is written to only while
  no other existing checkout has been seen using it.
- **Outside git, a config file declares a repository, and must name it.** A Perforce workspace has
  no `.git`, so `.dotnotes/dotnotes.json` above the directory is what makes it a repository at all.
  The chain has nothing else to fall back to but a hash of the path, which is what moving the
  workspace changes, so a config that names nothing refuses.
- **Nothing runs `git`.** Every call needs an identity, a process launch costs more than the reads
  it replaces, `git` need not be on the PATH of a server an editor started, and a child process
  would move the most important table in this repository out of the fast suite.
- **Provenance is on the answer.** `NamedBy` says which step of the chain named the repository,
  because a name that surprises its reader is otherwise reverse-engineered from the filesystem.
- **Nothing here is remembered between calls.** The two things resolution reads are exactly the two
  a person changes while a session is open: `git init` in a directory that was not a repository, and
  the committed config that opts one in to repository scope. A cached answer means the server keeps
  giving the old one until it is restarted, and for the config that is the worse half -- the refusal
  names the file to create, so following the instruction the tool just gave appears to do nothing
  and the person has no reason left to doubt they did it right. Measured at 190 microseconds inside
  a repository and 291 outside one, against a crawl of tens of milliseconds: the cache bought 0.3 ms
  a request and sold the only two answers it reports. Both cases are pinned by a test.
- **The evidence file is consulted, never trusted.** `RepositoryEvidence` remembers what each machine
  store was seen as, so a store is found again after the key moves. It never answers the key or the
  opt-in, and every store it names is checked on disk before it is used; deleting it loses the
  ability to notice a move and nothing else. A remembered answer that could go stale is exactly the
  cache the rule above removed. See
  [the decision](../decisions/a-renamed-repository-keeps-its-notes-until-the-move-is-made.md).
- **A parse kept against its file's stamp is not a remembered answer.** `GitIndex`, `RootCommits` and
  `CommittedStores` keep what they read until the length or write time of the file it came from
  changes, the same rule the search index is kept by. The rule above forbids an answer that outlives
  the change to what it describes; one keyed to that file's stamp cannot.
- **Root commits are a set, and are evidence rather than a key.** A monorepo has one per repository
  merged into it, a fork shares its upstream's, and a repository has none before its first commit.
  Matching is by intersection; treating them as one value misses the monorepo, and keying on them
  changes the key at the first commit.
