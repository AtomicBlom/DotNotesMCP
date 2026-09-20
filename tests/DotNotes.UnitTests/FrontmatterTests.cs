using DotNotes.Notes.Files;

namespace DotNotes.UnitTests;

/// <summary>
/// That a note's metadata is read correctly and its prose survives being read.
/// <para>
/// Every case here is a way of losing somebody's writing: a fence that never closes swallowing the
/// note into metadata, a horizontal rule in the body cutting it in half, a broken block taking a
/// whole store's search down with it.
/// </para>
/// </summary>
public sealed class FrontmatterTests
{
	[Test]
	public void A_block_is_read_and_the_body_starts_after_it()
	{
		var block = FrontmatterBlock.Split("---\nname: one\n---\nBody text.\n");

		block.Present.ShouldBeTrue();
		block.Yaml.ShouldBe("name: one");
		block.Body.ShouldBe("Body text.\n");
	}

	/// <summary>
	/// A fence that never closes is not metadata. Reading it as an unterminated block would take the
	/// note's prose for properties and lose it at the next write.
	/// </summary>
	[Test]
	public void An_unterminated_fence_is_all_body()
	{
		const string Text = "---\nname: one\nthe note continues forever\n";

		var block = FrontmatterBlock.Split(Text);

		block.Present.ShouldBeFalse();
		block.Body.ShouldBe(Text);
	}

	/// <summary>A horizontal rule is ordinary markdown, and must not be taken for a closing fence.</summary>
	[Test]
	public void A_rule_in_the_body_does_not_truncate_the_note()
	{
		var block = FrontmatterBlock.Split("---\nname: one\n---\nAbove.\n\n---\n\nBelow.\n");

		block.Yaml.ShouldBe("name: one");
		block.Body.ShouldContain("Above.");
		block.Body.ShouldContain("Below.");
	}

	/// <summary>A note with no metadata at all is still a note.</summary>
	[Test]
	public void A_file_with_no_block_is_all_body()
	{
		var block = FrontmatterBlock.Split("Just prose.\n");

		block.Present.ShouldBeFalse();
		block.Body.ShouldBe("Just prose.\n");
	}

	[Test]
	[Arguments("\n")]
	[Arguments("\r\n")]
	public void Either_line_ending_parses_and_is_reported(string lineEnding)
	{
		var text = string.Join(lineEnding, "---", "name: one", "---", "Body.", string.Empty);

		var block = FrontmatterBlock.Split(text);

		block.Yaml.ShouldBe("name: one");
		block.LineEnding.ShouldBe(lineEnding);
		block.Body.ShouldBe("Body." + lineEnding);
	}

	/// <summary>An editor that writes a byte-order mark must not stop the first fence being seen.</summary>
	[Test]
	public void A_byte_order_mark_does_not_hide_the_block()
	{
		var block = FrontmatterBlock.Split("﻿---\nname: one\n---\nBody.\n");

		block.Present.ShouldBeTrue();
		block.Yaml.ShouldBe("name: one");
	}

	[Test]
	public void Scalars_and_sequences_are_read()
	{
		var matter = NoteFrontmatter.Parse("""
			name: arm64-msbuild-locator
			tags: [arm64, msbuild]
			machines:
			  - surface-arm
			  - trinity
			""");

		matter.Scalar("name").ShouldBe("arm64-msbuild-locator");
		matter.Sequence("tags").ShouldBe(["arm64", "msbuild"]);
		matter.Sequence("machines").ShouldBe(["surface-arm", "trinity"]);
	}

	/// <summary>
	/// One value and a list of one are the same intent, and Obsidian accepts both, so a note that
	/// says <c>tags: arm64</c> must not read as having no tags.
	/// </summary>
	[Test]
	public void A_lone_value_reads_as_a_list_of_one() =>
		NoteFrontmatter.Parse("tags: arm64").Sequence("tags").ShouldBe(["arm64"]);

	/// <summary>
	/// A note is data, not configuration. One broken block must not take out a search across a whole
	/// store, and the prose underneath it is still worth finding.
	/// </summary>
	[Test]
	public void A_broken_block_is_reported_rather_than_thrown()
	{
		var matter = NoteFrontmatter.Parse("name: [unclosed\n  : :");

		matter.Error.ShouldNotBeNull();
		matter.Scalar("name").ShouldBeNull();
	}

	[Test]
	public void Enrichment_is_recognised_by_its_prefix()
	{
		NoteFrontmatter.Parse("name: one").IsEnriched.ShouldBeFalse();
		NoteFrontmatter.Parse("name: one\ndn-gist: a summary").IsEnriched.ShouldBeTrue();
	}

	/// <summary>
	/// Topics live in Obsidian's own tags so the person gets a tag pane, a graph node and something
	/// a Bases view can filter by. A key of our own would be invisible to all three.
	/// </summary>
	[Test]
	public void A_machine_topic_is_a_nested_tag_and_the_authored_ones_are_untouched()
	{
		var merged = NoteFrontmatter.MergeTopics(["arm64"], ["msbuild", "sdk-resolution"]);

		merged.ShouldBe(["arm64", "dn/msbuild", "dn/sdk-resolution"]);
	}

	/// <summary>
	/// Two entries for one word is two nodes in the graph and two rows in the tag pane, for a
	/// distinction the reader does not care about.
	/// </summary>
	[Test]
	public void A_topic_the_author_already_tagged_is_not_added_again() =>
		NoteFrontmatter.MergeTopics(["deploy"], ["deploy", "build"])
			.ShouldBe(["deploy", "dn/build"]);

	/// <summary>Re-indexing an unchanged note must rewrite nothing, so the merge is deterministic.</summary>
	[Test]
	public void Merging_the_same_topics_again_yields_the_same_tags()
	{
		var once = NoteFrontmatter.MergeTopics(["arm64"], ["msbuild"]);

		NoteFrontmatter.MergeTopics(once, ["msbuild"]).ShouldBe(once);
	}

	/// <summary>
	/// The author's tags are topics too. Treating them as a separate vocabulary would have the
	/// indexer declaring a topic as new when the person had already used it.
	/// </summary>
	[Test]
	public void Topics_are_every_tag_with_the_prefix_removed() =>
		NoteFrontmatter.Parse("tags: [arm64, dn/msbuild]").Topics()
			.ShouldBe(["arm64", "msbuild"]);

	/// <summary>
	/// A note's values are text, whatever YAML would otherwise make of them. This is the read half
	/// of the rule <see cref="YamlScalar"/> keeps on write: a description of <c>no</c> that comes
	/// back as a boolean is not merely the wrong type, it is a value that reads as absent, because
	/// every accessor here asks for a string.
	/// </summary>
	[Test]
	public void A_word_yaml_would_retype_is_still_text()
	{
		var matter = NoteFrontmatter.Parse("description: no\nversion: 10\nenabled: true");

		matter.Scalar("description").ShouldBe("no");
		matter.Scalar("version").ShouldBe("10");
		matter.Scalar("enabled").ShouldBe("true");
	}

	/// <summary>An empty value is absent, not the empty string, or a fallback never fires.</summary>
	[Test]
	public void A_key_with_no_value_reads_as_absent()
	{
		var matter = NoteFrontmatter.Parse("name: one\ndescription:");

		matter.Has("description").ShouldBeTrue();
		matter.Scalar("description").ShouldBeNull();
	}
}
