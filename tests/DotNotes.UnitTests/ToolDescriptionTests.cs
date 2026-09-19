using DotNotes.Contracts;

namespace DotNotes.UnitTests;

/// <summary>
/// That every tool says why to reach for it instead of the thing the caller already knows.
/// <para>
/// This is the binding rule written as a test. An agent reaching for the files is not choosing
/// badly between known options -- it does not know there was a choice, and the description is the
/// only place that can say so before the choice is made.
/// </para>
/// </summary>
public sealed class ToolDescriptionTests
{
	[Test]
	public void Every_tool_is_described() =>
		Surface.Listed().ShouldAllBe(tool => !string.IsNullOrWhiteSpace(tool.Description));

	/// <summary>
	/// Naming the alternative is the whole job. Each of these exists because a caller would
	/// otherwise reach for something that works but costs more, or quietly answers a worse question.
	/// </summary>
	[Test]
	[Arguments(ToolNames.Search, "reading files")]
	[Arguments(ToolNames.Search, "asking the user")]
	[Arguments(ToolNames.Read, "search")]
	[Arguments(ToolNames.Context, "worktree")]
	public void Says_what_the_caller_would_otherwise_have_done(string tool, string expected) =>
		Surface.Named(tool).Description!.ShouldContain(expected, Case.Insensitive);

	/// <summary>
	/// The premise, stated where a caller reads it. A note store that behaved per-worktree would be
	/// indistinguishable from the one this replaces, so the thing that is different has to be said.
	/// </summary>
	[Test]
	public void The_instructions_say_notes_are_keyed_to_the_repository()
	{
		var instructions = Surface.Instructions();

		instructions.ShouldContain("repository", Case.Insensitive);
		instructions.ShouldContain("worktree", Case.Insensitive);
	}

	/// <summary>
	/// A caller that thinks there is a setup call makes one, fails, and concludes the server is
	/// broken. Saying there is none is cheaper than the refusal that would otherwise teach it.
	/// </summary>
	[Test]
	public void The_instructions_say_there_is_no_setup_call() =>
		Surface.Instructions().ShouldContain("setup", Case.Insensitive);

	/// <summary>
	/// The trigger for writing one at all, which no per-tool description can carry because it fires
	/// when no tool is yet a candidate.
	/// </summary>
	[Test]
	public void The_instructions_say_what_is_worth_writing_down()
	{
		var instructions = Surface.Instructions();

		instructions.ShouldContain("gotcha", Case.Insensitive);
		instructions.ShouldContain("git history", Case.Insensitive);
	}

	/// <summary>
	/// The one behaviour of this search a caller cannot guess. Not knowing it means phrasing every
	/// query as an exact identifier, which is the one shape that would have worked anyway.
	/// </summary>
	[Test]
	public void The_query_argument_says_identifiers_match_by_their_parts()
	{
		var query = Surface.Named(ToolNames.Search).InputSchema
			.GetProperty("properties").GetProperty("query").GetProperty("description").GetString();

		query.ShouldNotBeNull();
		query.ShouldContain("parts", Case.Insensitive);
	}
}
