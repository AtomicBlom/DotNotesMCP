# Repository scope is opt-in; machine scope is not

**Decision.** `scope: machine` works in any directory, immediately, with no configuration.
`scope: repository` works only where `.dotnotes/dotnotes.json` exists, and refuses until it does,
naming the file to create.

**Why.** This is what lets the server be registered once, globally, with no arguments, and behave
sanely in every repository on the machine. Without the gate, an agent choosing repository scope on
first contact drops an untracked `.dotnotes/` into whichever work repository happened to be open,
and the first anyone hears about it is a dirty `git status`. Committing notes into a shared
repository changes what the team reads and reviews; that is the repository owner's decision.

**Why the gate is free.** The file that opens it is the file that names the repository, which is
step one of the naming chain and exists anyway. One committed file does both jobs, so opting in is
not a second thing to learn -- and the refusal prints the exact contents to write, with the name
already resolved from the remote.

**Why machine scope is not gated.** It writes nothing anybody else sees, into a store the person
configured or the default under their own profile. Gating it would mean the server does nothing at
all until configured, which is the shape that gets uninstalled: the first note has to be writable
in the first session, or the agent goes back to restating things and never comes back.

**Identity still resolves without the file**, by remote or by folder name, because machine-scope
notes are filed under the repository key and that has to work everywhere. Only the committed store
is gated.

**What it costs.** A per-repository setup step, paid once, by a person. That is the point rather
than the price: the step is the consent.

**What changes the answer.** If the refusal turns out to be something agents route around by
writing the file themselves, the gate is not a gate. Then it becomes something only a person can
do -- a command, not a tool -- and the refusal says so instead.
