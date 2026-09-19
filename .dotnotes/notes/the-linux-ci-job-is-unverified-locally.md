---
name: the-linux-ci-job-is-unverified-locally
description: The ubuntu CI job has never run; it exists to prove portability and may be red on its first run
scope: repository
type: project
repository: dotnotesmcp
tags:
  - ci
  - portability
created: 2026-09-19
updated: 2026-09-19
dn-gist: The ubuntu CI job has never been executed, so a red first run is a finding rather than a broken build.
dn-asks:
  - Has the Linux CI job ever run?
  - Why might the ubuntu job fail first time?
  - Is this tool Windows-only?
dn-entities:
  - PathCasing
  - StoreLock
dn-confidence: high
dn-index: 2/afc07512/c8c9542265d8
---
The three CI jobs were added from a Windows machine with no Linux available, so the ubuntu job is the one part of the build that has never been executed.

It is there on purpose: nothing in this repository is meant to be Windows-only, and the two places most likely to break are the ones that deliberately ask the platform a question rather than assuming an answer -- PathCasing, which reports whether the filesystem folds case, and StoreLock, which relies on FileShare.None being enforced rather than advisory.

**How to apply:** if that job is red on its first run, read it as a finding rather than a broken build. Look in PathCasing or StoreLock before looking at whichever test reported it. If it turns out this is a Windows-targeted tool after all, deleting the job is a legitimate answer -- but say so in a decision rather than leaving it failing.

See [[deploy-keeps-binaries-out-of-the-notes-folder]] for the other thing about this build that is not obvious from reading it.
