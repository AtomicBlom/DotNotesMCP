using DotNotes.Notes.Repositories;

namespace DotNotes.UnitTests;

/// <summary>
/// That a name is a directory name on every platform and a wikilink target in Obsidian, whichever
/// of those wrote it.
/// </summary>
public sealed class SlugTests
{
	[Test]
	[Arguments("RoseMCP", "rosemcp")]
	[Arguments("Rose MCP", "rose-mcp")]
	[Arguments("Rose_MCP", "rose-mcp")]
	[Arguments("rose--mcp", "rose-mcp")]
	[Arguments("  Rose  MCP  ", "rose-mcp")]
	[Arguments("Db.Primary", "db-primary")]
	[Arguments("net10.0", "net10-0")]
	[Arguments("-leading-and-trailing-", "leading-and-trailing")]
	public void A_name_becomes_lower_case_and_hyphens(string name, string expected) =>
		Slug.Of(name).ShouldBe(expected);

	/// <summary>
	/// A path built from an empty name lands somewhere unintended, and a caller cannot act on a name
	/// that is not there, so there is always a name.
	/// </summary>
	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("///")]
	[Arguments(null)]
	public void There_is_always_a_name(string? name) => Slug.Of(name).ShouldBe("unnamed");

	/// <summary>
	/// Windows will not create these whatever the extension, and a repository is entitled to be
	/// called aux. The failure would otherwise arrive at the first write rather than here.
	/// </summary>
	[Test]
	[Arguments("aux", "aux-repo")]
	[Arguments("CON", "con-repo")]
	[Arguments("com1", "com1-repo")]
	[Arguments("LPT9", "lpt9-repo")]
	public void A_reserved_device_name_is_not_a_directory_name(string name, string expected) =>
		Slug.Of(name).ShouldBe(expected);

	[Test]
	public void A_long_name_is_cut_to_something_a_path_can_hold()
	{
		var slug = Slug.Of(new string('a', 200));

		slug.Length.ShouldBe(64);
	}

	/// <summary>Cutting must not leave the hyphen the cut landed on.</summary>
	[Test]
	public void A_cut_name_does_not_end_in_a_hyphen() =>
		Slug.Of(new string('a', 64) + " tail").ShouldNotEndWith("-");
}
