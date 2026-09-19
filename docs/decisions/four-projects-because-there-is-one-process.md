# Four projects, because there is one process

**Decision.** `DotNotes.Contracts`, `DotNotes.Notes`, `DotNotes.Index`, `DotNotes.Server`, and one
test project. RoseMCP's eighteen-project layout is the model for the conventions in this repository
but not for its shape.

**Why not copy the shape.** Every one of Rose's splits is forced by something that does not exist
here. `Broker` and `Worker` are separate because analyzer assemblies cannot be unloaded and killing
a process is the only way to reclaim them. `Ui`, `Tray` and `Inspector` are `net10.0-windows`, which
a cross-platform test project cannot reference. `Ui.Core` and `XamlDiff` exist *because* of that, as
plain `net10.0` twins so the fast suite can see inside them. The three tap projects are native C++.
`XamlStubs` is loaded by Roslyn as an analyzer rather than referenced.

DotNotes is one managed process, one target framework, no UI, no native code, no analyzer loading.
Copying the shape would produce project files whose doc comments could not honestly say why they
exist, and Rose's own rule is that a project earns its boundary from a constraint.

**Why these four and not one.** Each boundary is load-bearing:

- **`Contracts` has no package references at all.** That is what lets the host, the index and every
  test reference it, and it is what lets `ToolBudgetTests` measure the whole model-facing surface
  without building a server. A type here that needed a package would put that package in front of
  all of them, so anything that reads disk or holds state belongs elsewhere.
- **`Notes` decides which store a call means without depending on what serves one.** The direct
  analogue of Rose's `Solutions` reading solution files without MSBuild, and the reason the identity
  table is a fast test.
- **`Index` is the only project that would grow a storage dependency.** There is none today -- the
  index is a dictionary built on first use -- and keeping the seam means adding one later does not
  reach the stores or the contracts.

**Why one test project.** Rose splits by cost, and its integration suite loads real solutions and
runs real design-time builds. Nothing here runs MSBuild or starts a child process; the slowest test
stages a directory in `%TEMP%`. Rose's own
`the-test-split-is-by-cost-and-dependency-not-by-disk` licenses exactly that in the fast suite.

**What changes the answer.** A second host -- a tray, or an HTTP transport with a UI reading live
state -- would make the registration path shared between two callers, which is the reason
`AddRoseMcpBroker` lives in a library rather than in a host. Then `AddDotNotes()` moves out of
`Server` and this becomes five.
