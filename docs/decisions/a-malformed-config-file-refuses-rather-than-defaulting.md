# A malformed config file refuses rather than defaulting

**Decision.** A *missing* configuration file means the defaults, always. A file that is *there* and
cannot be parsed throws, naming the file and what the parser objected to. This applies to both
`.dotnotes/dotnotes.json` and the machine `settings.json`.

**Why this parts company with Rose.** Rose treats a broken `rosemcp.json` as absent, and
`RoseSettingsFile` turns any failure into the defaults, on the reasoning that a preference file is
not load-bearing and a host that refused to start over hand-edited JSON would trade a small
inconvenience for a large one. That reasoning is right for a preference and wrong here, and the
difference is what the file decides.

Every value in a preference file has a working default, so defaulting past a typo costs the
preference. These two files decide *where data goes*:

- `.dotnotes/dotnotes.json` chooses the repository key. Defaulting past a typo falls through to the
  remote or the folder name, and the repository's notes are filed under a second name -- which is
  the fragmentation this server exists to remove, produced by the server itself.
- `settings.json` chooses the machine store. Defaulting past a typo writes notes under
  `%LOCALAPPDATA%` while the person is looking at a vault, and nothing says so.

Both relocate data rather than degrade behaviour, and both failures are silent and plausible: notes
are still written, still readable, still findable -- in the wrong place, discovered weeks later when
the other machine has none of them.

**Why a refusal is cheap here.** The file is hand-edited and rare. Someone who has just edited it is
the person best placed to fix it, and the message names the path and the parse error. The
alternative is not "it keeps working" but "it works somewhere else".

**Absent is still fine**, and is the ordinary case for both. Neither file has to exist; a machine
with no `settings.json` uses the default store, and a repository with no `dotnotes.json` has
machine scope and no committed store.

**What changes the answer.** A configuration file that grows a field nothing depends on for
placement. Then that field alone can degrade, and the refusal should narrow to the fields that
choose a location rather than covering the whole file.
