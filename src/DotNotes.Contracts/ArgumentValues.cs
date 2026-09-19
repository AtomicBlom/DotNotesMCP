namespace DotNotes.Contracts;

/// <summary>
/// The arguments that are an enum wearing a string, and the one refusal they share.
/// <para>
/// MCP carries these as text, and the tempting shape is a switch whose default is the common case.
/// That turns a typo into a confident answer to a different question: <c>scope: "repo "</c> quietly
/// searching both stores reads exactly like a repository with no notes, and the caller concludes
/// there is nothing to find rather than that they misspelled something. So the default is a refusal
/// that lists what is allowed.
/// </para>
/// </summary>
public static class ArgumentValues
{
	/// <summary>The refusal for an argument that takes one of a fixed set and was given something else.</summary>
	/// <param name="argument">The argument's name, as the caller spells it.</param>
	/// <param name="given">What arrived.</param>
	/// <param name="allowed">Every value that would have been accepted.</param>
	public static ArgumentException Unknown(string argument, string? given, params string[] allowed) =>
		new($"Unknown {argument} '{given}'. Use {string.Join(", ", allowed)}.");

	/// <summary>
	/// Which stores a read covers. Defaults to both, which is what a caller almost always means and
	/// costs nothing to get wrong: a read of the wrong store finds less, where a write to it cannot
	/// be undone.
	/// </summary>
	/// <exception cref="ArgumentException">The scope is not one of the three.</exception>
	public static StoreSelection ReadScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
	{
		null or "" or "both" => StoreSelection.Both,
		"machine" => StoreSelection.Machine,
		"repository" or "repo" => StoreSelection.Repository,
		_ => throw Unknown("scope", scope, "machine", "repository", "both"),
	};

	/// <summary>
	/// Which store a write lands in. No default, ever: guessing commits a machine-specific fact to a
	/// shared repository, and a pushed note cannot be recalled.
	/// </summary>
	/// <exception cref="ArgumentException">The scope is not one of the two.</exception>
	public static NoteScope WriteScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
	{
		"machine" => NoteScope.Machine,
		"repository" or "repo" => NoteScope.Repository,
		_ => throw Unknown("scope", scope, "machine", "repository"),
	};

	/// <summary>
	/// The kind of note a filter narrows to, or null for all of them. A filter that silently lost an
	/// unrecognised name would widen the answer rather than narrowing it, which is the opposite of
	/// what was asked.
	/// </summary>
	/// <exception cref="ArgumentException">The type is not one of the four.</exception>
	public static NoteType? Type(string? type) => type?.Trim().ToLowerInvariant() switch
	{
		null or "" => null,
		"project" => NoteType.Project,
		"user" => NoteType.User,
		"feedback" => NoteType.Feedback,
		"reference" => NoteType.Reference,
		_ => throw Unknown("type", type, "project", "user", "feedback", "reference"),
	};
}

/// <summary>Which stores a read covers.</summary>
public enum StoreSelection
{
	/// <summary>Both, which is what a caller almost always means.</summary>
	Both,

	Machine,

	Repository,
}
