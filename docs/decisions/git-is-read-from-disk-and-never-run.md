# Git is read from disk and never run

**Decision.** Repository identity comes from reading `.git`, `commondir` and `config` as files.
No `git` process is started, here or anywhere else in this server.

**Why not run git.** `git rev-parse --git-common-dir` answers the same question and is the obvious
thing to reach for. Four reasons it loses:

- **Every call needs an identity.** A process launch is tens of milliseconds on Windows and worse
  behind a virus scanner, against three to five `File.Exists` probes and at most four small reads.
  Paying that before every note read is a latency the caller feels, and a tool that feels slow is a
  tool an agent stops choosing.
- **`git` need not be on the PATH** of a server an editor launched. An MCP server inherits whatever
  environment its client had, which is not the shell the person configured.
- **A child process moves the most important test in this repository out of the fast suite.**
  `RepositoryIdentityTests` is a table with a staged directory layout per row; a process launch per
  row makes it something nobody runs on every change.
- **The parse is genuinely small.** `commondir` is one line. `config` is INI, and the only key that
  matters is one `url` in one section.

**What is deliberately absent.** No attempt to understand git's config beyond that one key: no
includes, no conditional includes, no `insteadOf` rewriting. Anything `GitConfigFile` cannot make
sense of is no remote, which falls through to the next step of the naming chain. Being wrong there
costs a less portable name, never a wrong one -- the folder name still identifies the repository on
this machine, and `.dotnotes/dotnotes.json` overrides both.

**What changes the answer.** A question that cannot be answered by reading a handful of files --
resolving a ref, listing branches, knowing whether a file is tracked. None of those is needed to
decide which store a call means, and if one becomes needed it should be asked of libgit2 rather than
of a process, so the answer stays in the fast suite.
