---
name: the-two-stores-answer-to-different-things
description: The machine store is keyed to the repository so a deleted worktree cannot take notes with it; the committed store follows the checkout so notes travel with the branch
scope: repository
type: project
repository: dotnotesmcp
tags:
  - stores
  - worktree
  - scope
created: 2026-09-21
updated: 2026-09-21
---
"Keyed to the repository" is the machine store's rule, and was mistakenly applied to both. The
correction, from Steven, is that the two stores protect against opposite failures:

- **The machine store** is untracked and nothing reconciles it. The pain is not two live worktrees
  disagreeing -- that happens, but it is the rarer case. The pain is that **a discarded worktree
  takes its notes with it**: the path stops matching anything, no key will ever resolve to them
  again, and there is no history to recover them from. So this store is keyed to the repository and
  outlives every checkout.
- **The repository store** is tracked, so git reconciles it already. It lives in the checkout the
  call came from, and a note commits on the branch that learned the fact, to be reviewed with the
  change it describes.

The case for routing both through the main checkout is a note written in one live worktree being
visible in another before it merges. That is real and rarer than it looks, and the answer to it is
to write the fact to machine scope as well -- deliberately, rather than every committed note paying
a branch mixing.

Full argument in `docs/decisions/the-committed-store-follows-the-checkout.md`. To see what a real
worktree resolves to, [[check-store-routing-with-a-throwaway-worktree]].
