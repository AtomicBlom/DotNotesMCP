# The environment is read through a seam

**Decision.** `NoteOptions.Environment` is a `Func<string, string?>` defaulting to
`Environment.GetEnvironmentVariable`. Every `DOTNOTES_*` read goes through it. Tests replace it;
nothing else does.

**Why.** The process environment is shared by everything running in the process, and a test suite
runs its tests in parallel. A test that set `DOTNOTES_STORE` to exercise the precedence chain
redirected *every other test running beside it* into one store directory, where they collided over
each other's files.

The failure that produced is the expensive kind. It named tests that had nothing to do with the
variable, it moved around the suite from run to run, and the count changed between runs of the same
build -- two failures, then ten. Nothing in the failing test's own code was wrong, so reading it
told you nothing. Only the temporary path in the exception gave it away: a note being written into a
directory called `fromEnvironment`, which no test of note hygiene has any reason to know about.

**Why not serialise those tests instead.** `[NotInParallel]` with a shared key was the first attempt
and it is not enough: it stops the two environment tests racing each other and does nothing about
the other two hundred running alongside. Serialising the whole suite against them would trade a
correctness problem for a slow one, and would leave the hazard in place for the next test that
reaches for a variable.

**It also removes a second class of flake.** With `Environment = _ => null` in every test harness, a
`DOTNOTES_STORE` genuinely set on the machine running the suite cannot change what a test resolves.
That failure would only ever appear on one developer's machine, which is the worst place for it.

**What it costs.** One property, and the discipline that a new `DOTNOTES_*` read goes through it
rather than reaching for `Environment` directly. The rule that each variable is read in exactly one
place is what keeps that enforceable.

**What changes the answer.** Nothing likely. If the reads ever grow past a handful the seam becomes
a small interface rather than a delegate, which is the same decision with more ceremony.
