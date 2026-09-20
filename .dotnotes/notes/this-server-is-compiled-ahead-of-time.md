---
name: this-server-is-compiled-ahead-of-time
description: AOT analysers are on for every project but the tests, so reflection-based JSON or YAML fails the ordinary build
scope: repository
type: project
repository: dotnotesmcp
tags:
  - aot
  - build
  - serialization
created: 2026-09-20
updated: 2026-09-20
---
The release ships as one native image per architecture. `IsAotCompatible` is set in
`Directory.Build.props` for everything except the tests, and warnings are errors, so a reflecting
call fails at the keystroke rather than at the next release.

**What that forbids, and what to use instead:**

- No `JsonSerializer.Deserialize<T>(text, options)`. Add the type to a `JsonSerializerContext` and
  use the overload taking its `JsonTypeInfo` -- passing a resolver to the reflecting overload
  silences nothing, because it is the overload that carries the annotation.
- No `YamlDotNet` `DeserializerBuilder`. `YamlBlock` walks the parser's event stream, which needs no
  reflection and fits better anyway: frontmatter has no schema, and a generated static deserializer
  needs exactly the declared types there are none of.

**Three things that bite quietly:**

1. **A new tool's result type must be listed in `ResultJson`**, or the published server throws while
   starting and names the type. A debug run finds it by reflection and says nothing, so this only
   ever shows up in a real build.
2. **An enum needs `UseStringEnumConverter` on the context.** The converter is chosen when the
   metadata is generated, not when it is used. Without it a published build sends `"scope": 1` where
   every other build sends `"scope": "Repository"` -- no warning, no failure, a wire format that
   depends on how the server was compiled.
3. **`init` on a JSON-bound property silently drops its initialiser.** The generator models init
   members as constructor parameters and assigns all of them at once, so a key absent from the file
   arrives as `default`. That is how `Notes = "notes"` became null. Use `set` on these DTOs.

**Building both architectures** needs `VC.Tools.x86.x64` *and* `VC.Tools.ARM64` in Visual Studio;
AOT cross-compiles fine between them on one machine, but with one toolset missing the message is
"Platform linker not found", which reads like a broken install rather than a missing checkbox.

See [[deploy-keeps-binaries-out-of-the-notes-folder]] for the other rule the packaging path has to
hold.
