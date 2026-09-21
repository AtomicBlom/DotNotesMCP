---
name: check-store-routing-with-a-throwaway-worktree
description: A detached worktree plus --explain is the only way to see which store a linked worktree actually resolves to
scope: repository
type: reference
repository: dotnotesmcp
tags:
  - git
  - diagnostics
  - worktree
created: 2026-09-21
updated: 2026-09-21
---
The unit tests stage git layouts as files rather than running git, which is deliberate and fast, but
it means a routing bug can pass the whole suite and still be wrong against a worktree git made. To
see the real answer:

```
git worktree add --detach <scratch>/wt-probe HEAD
dotnet run --project src/DotNotes.Server -- --explain <scratch>/wt-probe
git worktree remove --force <scratch>/wt-probe
```

`--explain` prints `worktree`, `root` and both store paths, which is the whole routing decision on
one screen. What to look for: **the machine store path must be identical from the probe and from the
main checkout, and the repository store path must differ.** Those two lines together are the split
that [[the-two-stores-answer-to-different-things]] exists for -- either one alone reads as correct
while the other is broken.

`--detach` matters. Without it `git worktree add` creates a branch, which is state to clean up in a
repository the probe is only supposed to read.
