---
name: ide0005-can-disagree-with-the-compiler
description: IDE0005 reported a using as unnecessary that the build then required; trust the compiler
scope: repository
type: project
repository: dotnotesmcp
tags:
  - analyzers
  - build
created: 2026-09-19
updated: 2026-09-19
dn-gist: IDE0005 can name the wrong using directive; the compiler decides which one is unused.
dn-asks:
  - Why did removing an unnecessary using break the build?
  - Can IDE0005 report the wrong using directive?
  - Which namespace does IMcpServerBuilder live in?
dn-topics:
  - analyzers
  - build
dn-entities:
  - IDE0005
  - IMcpServerBuilder
dn-confidence: high
dn-index: 2/afc07512/ce2e20cf5a60
---
`dotnet build` reported IDE0005 (unnecessary using) for `using Microsoft.Extensions.DependencyInjection;` in ToolErrorReporting.cs. Removing it broke the build: `IMcpServerBuilder` lives in that namespace.

The genuinely unused directive was `ModelContextProtocol.Server`, one line below. The analyzer named the wrong one.

**How to apply:** when IDE0005 and the compiler disagree, remove the directive it names, rebuild, and if that breaks put it back and look at its neighbours. Do not batch-remove what the analyzer lists.
