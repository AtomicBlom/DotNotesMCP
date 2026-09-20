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

## Ahead-of-time by default

**Decision.** `deploy.ps1 -Mode package` publishes Native AOT: one native image per architecture,
no runtime to install. `-FrameworkDependent` builds the small managed one. The everyday
`deploy.ps1` is unchanged and still framework-dependent, because AOT costs about forty seconds and
the dogfooding loop runs several times a day.

**Why, and it is the same argument twice.** An MCP server is started by an editor with no console
attached, so a missing runtime does not present as an error -- it presents as a server that is never
there. And startup halves, 127 ms to 67 ms. Both land on the rule that binds everywhere: an agent
that reaches for a tool and finds nothing, or finds it slow, goes back to reading files and does not
come back. One removes an absent server, the other removes a slow one.

**What it cost, and what it bought.** Four reflecting JSON call sites became source-generated
contexts, and the frontmatter reader stopped asking a deserializer to map YAML onto types --
[YamlBlock](../../src/DotNotes.Notes/Files/YamlBlock.cs) walks the parser's events instead. That
second one is not a concession: a note's frontmatter has no schema, so mapping it onto declared
types never fitted, and the generated static deserializer that would satisfy the analyzer needs
exactly the declared types there are none of.

| | self-contained | ahead-of-time |
|---|---|---|
| payload per architecture | 81.6 MB, 236 files | **15.3 MB, one file** |
| release zip | 72 MB | **10.7 MB** |
| installer | 50.3 MB | **8.9 MB** |
| cold start | 127 ms | **67 ms** |

**Why it stays correct.** `IsAotCompatible` is on for every project but the tests, so the analyzers
run at ordinary build rather than only at publish. Warnings are errors here, so a reflecting call
added on a Tuesday fails at the keystroke that caused it instead of being found by whoever next cuts
a release.

**What changes the answer.** A dependency that cannot be made trim-safe. The MCP SDK is clean today,
which was the one thing that could have settled this the other way.

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

**Why cross-compiling is fine.** Native AOT cross-compiles between architectures on one OS -- the
ILCompiler picks a host/target pair of MSVC tools, and `arm64_amd64` and `amd64_arm64` are paths it
codes for explicitly. Only cross-*OS* is out. So one machine, and one CI runner, builds both. It
does need **both** C++ toolsets installed, `VC.Tools.x86.x64` and `VC.Tools.ARM64`; with one
missing the message is "Platform linker not found", which reads like a broken installation rather
than a missing checkbox.

**Why no deduplication.** RoseMCP's packaging hoists whatever both architectures built identically
into a `shared` folder, and that pays there because most of its payload is architecture-neutral IL.
Here each architecture is a single native image with nothing in common to hoist, so the question
does not arise. It did not pay before AOT either: a self-contained publish is mostly the runtime,
compiled per architecture and so differing file by file -- 45 of 236 files were identical, 4.8 MB of
81.6.
