# What a model is told

Read before changing `ServerInstructions`, a tool description, or an argument's help.

This text is the product. An agent reaches for what it already knows, and a tool that works but was
never chosen is a bug of the same severity as one that answers wrongly.

- **Every description names the thing the caller would otherwise have done.** An agent reaching for
  the files is not choosing badly between known options -- it does not know there was a choice, and
  this is the only place that can say so before the choice is made. `ToolDescriptionTests` pins the
  phrase for each tool.
- **A fact goes where it is read at the moment it is needed.** The scope decision is on the `scope`
  argument rather than in the instructions, because the instructions load into every session
  including the many that never write a note, while the argument is read exactly when scope is being
  chosen. See [the decision](../decisions/a-fact-goes-where-it-is-read.md).
- **The budget is on the total, not on each tool.** Every session pays the whole surface before a
  single call. The question for any sentence is whether removing it changes what the agent does.
- **There is a floor as well as a ceiling.** A description too short to name the alternative loses
  to reading files, which costs far more than the characters it saved.
- **Instructions name only tools the session can see, and name all of them.** Naming one a client
  cannot see spends context teaching an approach that fails on the first call; leaving one out means
  it is found only by reading a list.
- **Reasoning goes in doc comments.** A maintainer needs to know why a rule exists; a model needs
  the rule. The two audiences are not the same and the text for them is not either.
- **The index mode is budgeted separately** because it is never served beside anything else. A
  session that sees it is there to do one thing several hundred times, and every sentence that makes
  two of those consistent pays for itself at once.
- **The index-mode instructions are kept short of nuance**, because their hash stales the whole
  corpus. Anything that might be tuned belongs in the exemplars, which are data and change freely.
