# The committed store is found rather than configured

**Decision.** A checkout's committed notes are wherever a DotNotes-generated `index.md` is, and finding
one is what opts the checkout in to repository scope. Inside git, `.dotnotes/dotnotes.json` is
optional: it pins a name, and it places the store where a repository wants to say so once. Outside
git it declares a repository -- a Perforce workspace, or anything else with no `.git` to find.

**Why the index.** It already marks itself, with `dotnotes-generated: true` and the generated-file
marker. Every folder of notes has one, and it moves with the folder, so a person who moves the notes
moves the beacon without knowing it is one. A path in a config file is a second record of where the
notes are, and it is the one that goes stale when somebody moves the folder and not the file.

**How it is found, without walking the tree.**

1. A path the evidence file remembers, verified by reading the marker there.
2. The tracked paths in `.git/index`, filtered to `index.md`, each checked for the marker. One linear
   read, no directory walk, no git process, and current as soon as a `git mv` is staged.
3. `.dotnotes/notes/`, the default.

There is no walk. An untracked store exists only before its first commit, and this server created it,
so the evidence file already knows where it is.

**Why a marked index is consent.** The gate exists so that a server registered globally never drops a
folder into a repository on an agent's initiative. A committed DotNotes index is somebody having
already decided to commit notes, and asking them to commit a config file saying so as well is the same
consent twice. A checkout with neither still refuses, and the refusal names `dotnotes init`, which
writes the empty marked index. That is no weaker a gate than the file it replaces, and no stronger:
an agent that can run a command can write a file.

**Several stores in one checkout.** A monorepo assembled from repositories that each had notes has one
store per original. Reads search every store in the checkout, because they are all this repository's
committed notes. A write goes to the nearest store enclosing the directory the session works in, then
to the one at the root, and refuses naming them only when there are several and none encloses. This
chooses a location, not an identity, so it does not contradict keying on the repository. A name in two
of them is reported by `note_check`, because `[[repo:name]]` can mean only one.

**Why `dotnotes.json` stays.** Outside git there is no `.git` to find and no index to parse, so a file
a person commits to that system is the only way to say "this directory is a repository, called this".
It is found by walking up from the working directory, as `.git` is, and the name is required there:
with no remote and no commits, the only other thing to key on is a hash of the path, which is exactly
what moving the workspace changes. Inside git it keeps its first job, a name a person pinned, which is
the step of the chain that does not move when an origin is added.

**What it costs.** A reader for the git index, versions 2 to 4 -- git's own documented format, read
and never written. Version 4 compresses each path against the one before it, so the read is
sequential. A sparse index lists directories outside the cone rather than files, and a store out
there is not checked out anyway.

**What would change the answer.** If marked indexes turn up where no person chose them, the marker has
become something agents write to get past the gate, and consent moves to something only a person can
do.
