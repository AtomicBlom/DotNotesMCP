---
name: measure-resolution-with-a-file-based-script
description: "A dotnet run file-based script with #:project times NoteStores.For in-process; --explain timings are all process startup"
scope: repository
type: reference
repository: dotnotesmcp
tags:
  - performance
  - diagnostics
  - resolution
created: 2026-09-24
updated: 2026-09-24
---
Timing `--explain` measures `dotnet run` startup (~1.2 s), not resolution. To time `RepositoryIdentity.For` / `NoteStores.For` in-process, write a file-based script outside the repo and run it with `dotnet run bench.cs -- <dirs>`:

```csharp
#:project C:\Dev\Personal\DotNotesMCP\src\DotNotes.Notes\DotNotes.Notes.csproj
using System.Diagnostics;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;
foreach (var dir in args)
{
    var options = new NoteOptions { DefaultRoot = dir };
    NoteStores.For(dir, options);                 // warm the stamp caches
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < 2000; i++) NoteStores.For(dir, options);
    Console.WriteLine($"{dir}: {sw.Elapsed.TotalMicroseconds / 2000:0} us");
}
```

Runs are noisy by ~100 us; compare several runs, not one.

Figures when pairs landed (2026-09-24), warm: identity ~150-200 us, both stores ~350-450 us. The expensive parts had been the commit-graph stamp (now only the single file and the chain are stat'd) and the reflog read (now read only when the graph lists no roots).
