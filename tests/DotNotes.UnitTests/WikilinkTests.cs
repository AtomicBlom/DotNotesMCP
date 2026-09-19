using DotNotes.Contracts;
using DotNotes.Notes.Files;

namespace DotNotes.UnitTests;

/// <summary>
/// That the links found in a note are the ones somebody meant to write.
/// <para>
/// The code-skipping half is what keeps <c>note_check</c> honest. A note explaining this syntax
/// quotes it, and counting the quotation produces dangling targets nobody wrote and backlinks
/// between notes that never mention each other -- which is a report people stop reading.
/// </para>
/// </summary>
public sealed class WikilinkTests
{
	[Test]
	public void A_bare_target_is_found()
	{
		var links = Wikilink.In("See [[tray-dev-loop]] for the rest.");

		var link = links.ShouldHaveSingleItem();
		link.Target.ShouldBe("tray-dev-loop");
		link.Scope.ShouldBeNull();
		link.Alias.ShouldBeNull();
	}

	[Test]
	public void An_alias_is_kept_apart_from_the_target()
	{
		var link = Wikilink.In("See [[rosemcp/tray-dev-loop|the tray loop]].").ShouldHaveSingleItem();

		link.Target.ShouldBe("rosemcp/tray-dev-loop");
		link.Alias.ShouldBe("the tray loop");
	}

	/// <summary>A link across stores names the store, because Obsidian cannot resolve one and we can.</summary>
	[Test]
	[Arguments("[[machine:arm64-quirk]]", NoteScope.Machine, "arm64-quirk")]
	[Arguments("[[repo:tunit-opt-in]]", NoteScope.Repository, "tunit-opt-in")]
	public void A_prefixed_target_names_its_store(string body, NoteScope scope, string target)
	{
		var link = Wikilink.In(body).ShouldHaveSingleItem();

		link.Scope.ShouldBe(scope);
		link.Target.ShouldBe(target);
	}

	[Test]
	public void Several_links_come_back_in_order()
	{
		var links = Wikilink.In("[[one]] then [[two]] then [[three]]");

		links.Select(link => link.Target).ShouldBe(["one", "two", "three"]);
		links[0].Start.ShouldBeLessThan(links[1].Start);
	}

	/// <summary>A note explaining the syntax quotes it, and a quotation is not a link.</summary>
	[Test]
	public void A_link_inside_a_fenced_block_is_not_a_link()
	{
		var body = string.Join('\n', "Write it like this:", "```", "[[not-a-link]]", "```", "[[a-link]]");

		Wikilink.In(body).Select(link => link.Target).ShouldBe(["a-link"]);
	}

	[Test]
	public void A_link_inside_inline_code_is_not_a_link() =>
		Wikilink.In("Type `[[not-a-link]]` to link, as in [[a-link]].")
			.Select(link => link.Target)
			.ShouldBe(["a-link"]);

	/// <summary>A tilde fence is a fence, and a fence that never closes runs to the end.</summary>
	[Test]
	public void A_tilde_fence_and_an_unclosed_fence_are_both_code()
	{
		Wikilink.In("~~~\n[[no]]\n~~~\n[[yes]]").Select(link => link.Target).ShouldBe(["yes"]);
		Wikilink.In("[[yes]]\n```\n[[no]]\n").Select(link => link.Target).ShouldBe(["yes"]);
	}

	/// <summary>
	/// Markdown that merely looks like a link is not one. Note that <c>a[[0]]</c> is deliberately
	/// absent: Obsidian reads that as the text "a" followed by a link to "0", and disagreeing with
	/// the renderer about what is a link is how a backlink appears in one and not the other.
	/// </summary>
	[Test]
	[Arguments("[[]]")]
	[Arguments("An unclosed [[link")]
	[Arguments("A [single] bracket")]
	[Arguments("A [[link\nbroken over lines]]")]
	public void What_is_not_a_link_is_not_found(string body) =>
		Wikilink.In(body).ShouldBeEmpty(body);

	/// <summary>The offsets are what a rename rewrites, so they have to address the whole link.</summary>
	[Test]
	public void The_span_covers_the_whole_link()
	{
		const string Body = "See [[one|first]] here.";

		var link = Wikilink.In(Body).ShouldHaveSingleItem();

		Body.Substring(link.Start, link.Length).ShouldBe("[[one|first]]");
	}

	[Test]
	[Arguments("one", null, null, "[[one]]")]
	[Arguments("one", null, "first", "[[one|first]]")]
	public void A_link_is_written_back_the_way_it_is_read(
		string target,
		NoteScope? scope,
		string? alias,
		string expected)
	{
		Wikilink.Write(target, scope, alias).ShouldBe(expected);

		Wikilink.In(expected).ShouldHaveSingleItem().Rendered.ShouldBe(expected);
	}

	[Test]
	public void A_cross_store_link_is_written_with_its_prefix() =>
		Wikilink.Write("arm64-quirk", NoteScope.Machine, null).ShouldBe("[[machine:arm64-quirk]]");
}
