# The installer cannot remove the notes

**Decision.** The payload installs to `bin` inside the product folder, and nothing any install or
uninstall path does reaches a sibling of it. An uninstall removes `bin` and `locks`; `-Purge` also
removes `settings.json`; **no flag removes the notes, and none is offered**.

**Why this is not ordinary caution.** The machine store exists precisely for what must not be
committed -- paths, machine quirks, anything naming a person or a customer. That is the same as
saying it is the only data on the machine with no copy in a remote, in a repository, or in anybody
else's checkout. Every other store this product touches can be recovered from git. This one cannot
be recovered from anything.

So the usual shape of an uninstaller -- remove the install directory, offer a switch that also
removes user data -- is wrong here twice over. The install directory *is* the data directory by
default, so `Remove-Item -Recurse` on the root is a data-loss bug rather than a tidy-up. And a
`-Purge` switch that means "everything" on other products would mean "delete the notes" on this one,
which is not a thing somebody should be able to do by reaching for a familiar flag.

`settings.json` is kept on an ordinary uninstall for an ordinary reason: somebody uninstalling to
install a newer build should not lose what they configured. `locks` goes either way, being transient
and nobody's.

**How the rule is held rather than remembered.** `Clear-InstallPayload` in `DotNotes.Deploy.ps1`
deletes one directory and is the only thing that deletes anything. The Inno script's
`[UninstallDelete]` names `bin`, `locks`, and `dirifempty` on the root -- there is deliberately no
recursive entry for `{app}`. Both installers call the same PowerShell, because a second copy of the
deletion rules is a second set of rules and only one of them gets fixed. What that failure looks
like is not a build error but a version of the uninstaller that removes a store the other one
spares.

## Self-contained by default

**Decision.** `deploy.ps1 -Mode package` publishes self-contained. `-FrameworkDependent` builds the
small one.

**Why.** An MCP server is started by an editor, with no console attached. A missing runtime
therefore does not present as an error message -- it presents as a server that is simply never
there. That is the one failure this product cannot afford, and it is already written down as a rule
that binds everywhere: an agent that reaches for a tool and gets nothing goes back to reading files
and does not come back. Trading 50 MB of download for removing that failure mode entirely is not a
close call.

The framework-dependent package still exists because it is a fiftieth of the size and a machine with
the SDK on it -- which is every machine this is developed on -- loses nothing by using it.
`Get-PrerequisiteProblem` reports a missing runtime only for that package, and reports rather than
refuses: somebody installing onto a machine they are about to finish setting up is doing a
reasonable thing.

## One installer, both architectures

**Decision.** The package carries `win-x64` and `win-arm64`, and the install picks by reading the
machine.

**Why.** This product exists because its author works on an x64 machine and an ARM64 machine and
accumulates quirks specific to each. Shipping something that can be installed wrong on half of them
would be a poor joke. And on Windows, installing the wrong one does not fail: an x64 build runs on
ARM64 under emulation, more slowly, and nothing says so. A silent wrong answer is the failure mode
this whole repository is organised against.

`Get-PeMachine` reads the machine word out of the PE header at packaging time and again at install
time. Both, because packaging is where a staging mistake is still "the build is wrong" and install
is the last point where it is still "the download is wrong".

**Why no deduplication.** RoseMCP's packaging hoists whatever both architectures built identically
into a `shared` folder, and that pays there because most of its payload is architecture-neutral IL.
It would not pay here: a self-contained publish is mostly the runtime, which is compiled ahead of
time per architecture and so differs file by file. A step that can silently produce a half-tree, in
exchange for very little, is a step not worth having.

**What changes the answer.** A third architecture, or a payload that grows enough that the download
size starts to matter. Then deduplication earns its complexity and can be added to packaging alone,
because the installer already treats the payload as opaque.
