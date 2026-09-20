---
name: nothing-about-the-filesystem-is-cached-between-calls
description: Repository identity is resolved fresh every call, because git init and the opt-in config both happen while a session is open
scope: repository
type: project
repository: dotnotesmcp
tags:
  - caching
  - git
  - stability
created: 2026-09-20
updated: 2026-09-20
---
`RepositoryIdentity.For` reads the disk on every call and keeps nothing. Do not reintroduce a cache
without reading this.

It had one, justified by "a repository does not move while a process is running". That is true of a
repository that exists, and false of the two cases that actually happen:

1. **A directory becomes a repository.** An agent finds it is not one, runs `git init`, asks again,
   and gets the remembered "no repository" until the server restarts. Found by installing a release
   on a second machine and using it, not by any test.
2. **The opt-in config appears.** Worse, because the refusal for repository scope *names the file to
   create*. You create it, nothing changes, and there is no reason left to doubt you did it right.
   A tool that describes a fix which then does not work is worse than one that describes nothing.

**The cost of not caching was measured, not assumed:** 190 microseconds inside a repository, 291
outside one, one or two resolutions per tool call, against a store crawl of tens of milliseconds.
The cache bought about 0.3 ms per request in exchange for being wrong about the only two things
identity reports.

The general shape is worth keeping in mind anywhere else here: this server's whole job is answering
questions about the state of a filesystem somebody else is editing at the same time. Memoising an
answer is memoising a claim about a file that was true once. The notes already work this way -- they
are the truth and the index rebuilds from them -- and identity should not have been the exception.

See [[this-server-is-compiled-ahead-of-time]] for the other thing that only showed up in a real
build rather than in a test.
