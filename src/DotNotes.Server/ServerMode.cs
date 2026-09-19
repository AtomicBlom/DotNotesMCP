namespace DotNotes.Server;

/// <summary>
/// Which surface the host serves. Two, and never both at once: see
/// <c>docs/decisions/the-indexing-mode-declares-only-its-own-tools.md</c>.
/// </summary>
public enum ServerMode
{
	/// <summary>The note tools, which is what an ordinary session wants.</summary>
	Serve,

	/// <summary>The enrichment loop, and nothing else.</summary>
	Index,
}
