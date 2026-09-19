using DotNotes.Index;

namespace DotNotes.UnitTests;

/// <summary>
/// That an extract reads like prose.
/// <para>
/// It is quoted back by a model deciding whether to open the note, so markup that only means
/// something to a renderer is noise -- and markup half removed is worse than markup left alone,
/// because it reads as a typo in the note rather than as a artefact of the extract.
/// </para>
/// </summary>
public sealed class SnippetTests
{
	private static readonly string[] Terms = ["build"];

	[Test]
	public void An_inline_code_span_loses_both_of_its_backticks() =>
		Snippet.Of("Run `dotnet build` first.", Terms).ShouldBe("Run dotnet build first.");

	[Test]
	public void Bold_loses_both_of_its_markers() =>
		Snippet.Of("**Why:** the build needs it.", Terms).ShouldBe("Why: the build needs it.");

	/// <summary>
	/// The terms these notes are about are identifiers, and an underscore is part of one. Dropping it
	/// would turn the discriminating word of a note into something nobody wrote.
	/// </summary>
	[Test]
	public void An_underscore_inside_an_identifier_survives() =>
		Snippet.Of("The build uses snake_case_names here.", Terms)
			.ShouldContain("snake_case_names");

	[Test]
	public void A_heading_marker_goes_and_a_hash_inside_a_word_stays()
	{
		Snippet.Of("# The build\n\nIt works.", Terms).ShouldBe("The build It works.");
		Snippet.Of("Issue C#12 in the build.", Terms).ShouldContain("C#12");
	}

	[Test]
	public void A_blockquote_marker_goes() =>
		Snippet.Of("> The build is fine.", Terms).ShouldBe("The build is fine.");

	/// <summary>Whitespace collapses, because an extract is one line inside a result.</summary>
	[Test]
	public void Whitespace_collapses_to_single_spaces() =>
		Snippet.Of("The\n\n  build\tworks.", Terms).ShouldBe("The build works.");

	/// <summary>A short note is returned whole rather than ellipsed for no reason.</summary>
	[Test]
	public void A_short_note_comes_back_whole() =>
		Snippet.Of("Short.", Terms).ShouldBe("Short.");

	/// <summary>
	/// Around the match rather than from the start: the sentence that explains why the note matched
	/// is what tells a reader whether to open it, and it is rarely the first one.
	/// </summary>
	[Test]
	public void A_long_note_is_cut_around_what_matched()
	{
		var body = new string('x', 400) + " the build broke here " + new string('y', 400);

		var extract = Snippet.Of(body, Terms);

		extract.ShouldContain("the build broke here");
		extract.Length.ShouldBeLessThan(body.Length);
	}

	[Test]
	public void A_cut_passage_says_it_was_cut() =>
		Snippet.Of(new string('x', 500) + " tail", Terms).ShouldEndWith("…");
}
