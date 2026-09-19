using DotNotes.Index;

namespace DotNotes.UnitTests;

/// <summary>
/// That an identifier is findable by its name and by the words in it.
/// <para>
/// This is the table that justifies not taking an index off the shelf. The terms that tell these
/// notes apart are project-specific identifiers, and the person searching six weeks later types the
/// words, not the identifier. An index that does one or the other finds half of them.
/// </para>
/// </summary>
public sealed class TokenizerTests
{
	/// <summary>The case the whole design turns on.</summary>
	[Test]
	public void An_identifier_is_findable_by_its_parts_and_by_itself()
	{
		var terms = Tokenizer.Of("GeneratedBindableCustomProperty");

		terms.ShouldContain("generatedbindablecustomproperty");
		terms.ShouldContain("bindable");
		terms.ShouldContain("property");
	}

	[Test]
	[Arguments("Db.Primary", "db.primary")]
	[Arguments("snake_case_name", "snake_case_name")]
	[Arguments("kebab-case-name", "kebab-case-name")]
	[Arguments("net10.0", "net10.0")]
	public void A_joined_identifier_keeps_its_whole_form(string text, string whole) =>
		Tokenizer.Of(text).ShouldContain(whole);

	[Test]
	[Arguments("Db.Primary", "db")]
	[Arguments("Db.Primary", "primary")]
	[Arguments("snake_case_name", "case")]
	[Arguments("kebab-case-name", "kebab")]
	[Arguments("net10.0", "net10")]
	public void A_joined_identifier_is_findable_by_a_segment(string text, string segment) =>
		Tokenizer.Of(text).ShouldContain(segment);

	/// <summary>
	/// A run of capitals belongs to the word the following lower-case letter starts, or the segment
	/// yields "ibindable" and "xmlhttp" and nobody's query says either.
	/// </summary>
	[Test]
	[Arguments("IBindableCustomPropertyImplementation", "bindable")]
	[Arguments("IBindableCustomPropertyImplementation", "implementation")]
	[Arguments("XMLHttpRequest", "xml")]
	[Arguments("XMLHttpRequest", "http")]
	[Arguments("XMLHttpRequest", "request")]
	public void An_acronym_ends_where_the_next_word_begins(string text, string expected) =>
		Tokenizer.Of(text).ShouldContain(expected);

	[Test]
	[Arguments("arm64", "arm")]
	[Arguments("arm64", "64")]
	[Arguments("net10", "net")]
	public void Letters_and_digits_are_separate_words(string text, string expected) =>
		Tokenizer.Of(text).ShouldContain(expected);

	/// <summary>
	/// A search for the words has to reach the note that only ever wrote the identifier. This is the
	/// behaviour, stated as the query it serves.
	/// </summary>
	[Test]
	public void A_query_of_words_reaches_a_note_that_wrote_only_the_identifier()
	{
		var indexed = Tokenizer.Of("Use [GeneratedBindableCustomProperty] on the view model.");
		var asked = Tokenizer.Query("bindable property");

		asked.ShouldAllBe(term => indexed.Contains(term));
	}

	/// <summary>Punctuation must not make the last word of a sentence a different term.</summary>
	[Test]
	public void A_trailing_stop_is_not_part_of_the_word()
	{
		Tokenizer.Of("the worker.").ShouldContain("worker");
		Tokenizer.Of("the worker.").ShouldNotContain("worker.");
	}

	/// <summary>
	/// Every term comes out lower-cased, so a query matches whatever case it was typed in. Case is
	/// still what marks the word boundaries, which is why the two spellings below do not yield the
	/// same terms -- <c>MSBuildLocator</c> can be segmented and <c>msbuildlocator</c> cannot, and
	/// pretending otherwise would mean either losing the segments or inventing them.
	/// </summary>
	[Test]
	public void Terms_are_lower_cased_but_case_is_what_marks_a_boundary()
	{
		var camel = Tokenizer.Of("MSBuildLocator");

		camel.ShouldAllBe(term => term == term.ToLowerInvariant());
		camel.ShouldBe(["msbuildlocator", "ms", "build", "locator"]);
		Tokenizer.Of("msbuildlocator").ShouldBe(["msbuildlocator"]);
	}

	/// <summary>A query typed in any case reaches the identifier as written.</summary>
	[Test]
	[Arguments("MSBUILDLOCATOR")]
	[Arguments("msbuildlocator")]
	[Arguments("MSBuildLocator")]
	public void A_query_matches_whatever_case_it_is_typed_in(string query) =>
		Tokenizer.Of("Uses MSBuildLocator to find the SDK.")
			.ShouldContain(Tokenizer.Query(query)[0]);

	[Test]
	public void Ordinary_prose_yields_its_words() =>
		Tokenizer.Of("pins the SDK architecture first")
			.ShouldContain("architecture");

	[Test]
	[Arguments("")]
	[Arguments(null)]
	[Arguments("   ")]
	[Arguments("...")]
	public void Nothing_yields_nothing(string? text) => Tokenizer.Of(text).ShouldBeEmpty();

	/// <summary>A query is the same reading, without repeats, so a doubled word is not weighted twice.</summary>
	[Test]
	public void A_query_drops_repeats() =>
		Tokenizer.Query("property property").Count(term => term == "property").ShouldBe(1);

	/// <summary>Term frequency is real for a document, so repeats are kept there.</summary>
	[Test]
	public void A_document_keeps_repeats() =>
		Tokenizer.Of("property property").Count(term => term == "property").ShouldBe(2);
}
