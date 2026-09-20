using DotNotes.Contracts;
using DotNotes.Index;
using DotNotes.Index.Enrichment;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Stores;

namespace DotNotes.UnitTests;

/// <summary>
/// The enrichment loop: that it hands out work, takes it back, and finishes.
/// <para>
/// Finishing is the property worth guarding hardest. Every part of this design conspires against a
/// loop that terminates -- the enrichment is written into the note it describes, so a hash taken
/// carelessly makes each note stale the moment it is finished, and nothing about the failure looks
/// like a bug until somebody notices the bill.
/// </para>
/// </summary>
public sealed class IndexRunTests
{
	private sealed record Harness(IndexRun Run, NoteStores Stores, string Store, GitFixture Fixture)
		: IDisposable
	{
		public void Dispose() => Fixture.Dispose();
	}

	private static Harness Create(params string[] names)
	{
		var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");


		var options = new NoteOptions
		{
			DefaultRoot = checkout,
			LocalAppData = GitFixture.Under(fixture.Root, "localappdata"),
			Environment = _ => null,
		};

		var store = GitFixture.Under(
			MachineSettingsFile.DirectoryFor(options.LocalAppData), "notes", "rosemcp");

		foreach (var name in names)
		{
			File.WriteAllText(
				Path.Combine(store, $"{name}.md"),
				$"---\nname: {name}\ndescription: About {name}\n---\nThe body of {name}.\n");
		}

		var search = new CrawlingNoteSearch(options);

		return new Harness(
			new IndexRun(options, search, "the instructions"),
			NoteStores.For(checkout, options),
			store,
			fixture);
	}

	private static Enrichment Good(string gist = "Something true about the subject.") => new()
	{
		Gist = gist,
		Asks = ["What is this about?", "Why does it matter?", "When does it apply?"],
		Topics = ["one", "two"],
	};

	private static string Claim(Harness harness) =>
		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).Note!.Lease;

	[Test]
	public void An_empty_store_is_drained_immediately()
	{
		using var harness = Create();

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).State.ShouldBe(AssignmentState.Drained);
	}

	[Test]
	public void A_note_with_no_enrichment_is_handed_out()
	{
		using var harness = Create("one");

		var assignment = harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

		assignment.State.ShouldBe(AssignmentState.Assigned);
		assignment.Note!.Name.ShouldBe("one");
		assignment.Note.Reason.ShouldBe(StaleReason.NeverIndexed);
		assignment.Progress.NeverIndexed.ShouldBe(1);
	}

	/// <summary>
	/// The property the whole mode turns on. The enrichment is written into the note it describes,
	/// so a hash over the whole file would make every note stale the instant it was finished, and the
	/// loop would run forever without anything looking wrong.
	/// </summary>
	[Test]
	public void An_enriched_note_is_not_handed_out_again()
	{
		using var harness = Create("one");

		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), ["one", "two"]);

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).State.ShouldBe(AssignmentState.Drained);
	}

	[Test]
	public void The_whole_store_drains()
	{
		using var harness = Create("one", "two", "three");

		for (var note = 0; note < 3; note++)
		{
			var assignment = harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

			assignment.State.ShouldBe(AssignmentState.Assigned);
			harness.Run.Write(harness.Stores, NoteScope.Machine, assignment.Note!.Lease, Good(), ["one", "two"]);
		}

		var final = harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

		final.State.ShouldBe(AssignmentState.Drained);
		final.Progress.Fresh.ShouldBe(3);
		final.Progress.Remaining.ShouldBe(0);
	}

	/// <summary>An edit is exactly what should bring a note back.</summary>
	[Test]
	public void Editing_a_note_puts_it_back_in_the_queue()
	{
		using var harness = Create("one");

		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), ["one", "two"]);

		var path = Path.Combine(harness.Store, "one.md");

		File.WriteAllText(path, File.ReadAllText(path).Replace("The body", "A different body", StringComparison.Ordinal));

		var assignment = harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

		assignment.State.ShouldBe(AssignmentState.Assigned);
		assignment.Note!.Reason.ShouldBe(StaleReason.SourceChanged);
	}

	/// <summary>Changing the brief stales the corpus, which is correct and is why the text is kept stable.</summary>
	[Test]
	public void Changing_the_instructions_puts_everything_back_in_the_queue()
	{
		using var harness = Create("one");

		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), ["one", "two"]);

		var other = new IndexRun(
			new NoteOptions
			{
				DefaultRoot = harness.Stores.Repository.Origin,
				LocalAppData = GitFixture.Under(harness.Fixture.Root, "localappdata"),
				Environment = _ => null,
			},
			new CrawlingNoteSearch(new NoteOptions
			{
				LocalAppData = GitFixture.Under(harness.Fixture.Root, "localappdata"),
				Environment = _ => null,
			}),
			"different instructions");

		other.Next(harness.Stores, NoteScope.Machine, 10).Note!.Reason.ShouldBe(StaleReason.PromptChanged);
	}

	/// <summary>The body and the author's own properties are not the indexer's to touch.</summary>
	[Test]
	public void Enrichment_leaves_the_body_and_the_authored_fields_alone()
	{
		using var harness = Create("one");
		var path = Path.Combine(harness.Store, "one.md");
		var before = File.ReadAllText(path);

		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), ["one", "two"]);

		var after = File.ReadAllText(path);

		after.ShouldContain("The body of one.");
		after.ShouldContain("description: About one");
		FrontmatterBlock.Split(after).Body.ShouldBe(FrontmatterBlock.Split(before).Body);
	}

	[Test]
	public void A_second_claim_gets_a_different_note()
	{
		using var harness = Create("one", "two");

		var first = harness.Run.Next(harness.Stores, NoteScope.Machine, 10).Note!.Name;
		var second = harness.Run.Next(harness.Stores, NoteScope.Machine, 10).Note!.Name;

		second.ShouldNotBe(first);
	}

	/// <summary>Everything claimed and nothing left is Blocked, which is not the same as done.</summary>
	[Test]
	public void Every_note_claimed_is_blocked_rather_than_drained()
	{
		using var harness = Create("one");

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

		var again = harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

		again.State.ShouldBe(AssignmentState.Blocked);
		again.BlockedReason.ShouldNotBeNull();
	}

	[Test]
	public void Releasing_a_claim_puts_the_note_back()
	{
		using var harness = Create("one");
		var lease = Claim(harness);

		harness.Run.Skip(harness.Stores, NoteScope.Machine, lease, SkipDisposition.Release, null);

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).State.ShouldBe(AssignmentState.Assigned);
	}

	[Test]
	public void A_note_answered_as_not_worth_indexing_is_not_asked_about_again()
	{
		using var harness = Create("one");

		harness.Run.Skip(
			harness.Stores, NoteScope.Machine, Claim(harness), SkipDisposition.NotWorthIndexing, "a stub");

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).State.ShouldBe(AssignmentState.Drained);
	}

	/// <summary>A loop that cannot finish is worse than a corpus with three unindexed notes in it.</summary>
	[Test]
	public void A_note_that_keeps_failing_is_eventually_left_alone()
	{
		using var harness = Create("one");

		for (var attempt = 0; attempt < 3; attempt++)
		{
			var assignment = harness.Run.Next(harness.Stores, NoteScope.Machine, 10);

			assignment.State.ShouldBe(AssignmentState.Assigned);
			harness.Run.Skip(
				harness.Stores, NoteScope.Machine, assignment.Note!.Lease, SkipDisposition.Unreadable, "no");
		}

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).State.ShouldBe(AssignmentState.Drained);
	}

	[Test]
	public void A_lease_that_was_never_issued_is_refused() =>
		Should.Throw<ArgumentException>(
				() => Create("one").Run.Write(
					Create("one").Stores, NoteScope.Machine, "made:one:up", Good(), []))
			.Message.ShouldNotBeEmpty();

	/// <summary>
	/// Consistency is the value, so the shape is a contract rather than a request. Each of these
	/// arrives as a refusal naming the field, which an agent corrects in one turn.
	/// </summary>
	[Test]
	public void A_misshapen_enrichment_is_refused_by_field()
	{
		using var harness = Create("one");

		// One claim for all of them: a refused write does not release its lease, which is what lets
		// an agent correct itself and submit again rather than having to ask for the note back.
		var lease = Claim(harness);

		Refuses(harness, lease, Good() with { Gist = new string('x', 200) }, "gist");
		Refuses(harness, lease, Good() with { Gist = "This note describes the thing." }, "This note");
		Refuses(harness, lease, Good() with { Asks = ["Only one?"] }, "asks");
		Refuses(harness, lease, Good() with { Asks = ["a?", "b?", "not a question"] }, "question");
		Refuses(harness, lease, Good() with { Topics = ["only-one"] }, "topics");
		Refuses(harness, lease, Good() with { Topics = ["One", "two"] }, "lower case");

		// And the same lease still works once the shape is right.
		harness.Run.Write(harness.Stores, NoteScope.Machine, lease, Good(), ["one", "two"]);
	}

	/// <summary>
	/// Without this, five hundred notes produce five hundred topics and the facet is worthless. The
	/// point is not to forbid a new topic but to make adding one deliberate.
	/// </summary>
	[Test]
	public void A_topic_outside_the_vocabulary_is_refused_until_it_is_declared()
	{
		using var harness = Create("one", "two");
		var lease = Claim(harness);

		Should.Throw<ArgumentException>(
				() => harness.Run.Write(harness.Stores, NoteScope.Machine, lease, Good(), []))
			.Message.ShouldContain("newTopics");

		var accepted = harness.Run.Write(harness.Stores, NoteScope.Machine, lease, Good(), ["one", "two"]);

		accepted.TopicsAdded.ShouldBe(["one", "two"]);

		// Once in the vocabulary, the next note reuses them without declaring anything.
		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), []);
	}

	/// <summary>
	/// An unresolved wikilink in Obsidian is an invitation to create that note. An indexer should not
	/// leave several hundred of those behind.
	/// </summary>
	[Test]
	public void A_link_to_a_note_that_is_not_there_is_dropped_rather_than_written()
	{
		using var harness = Create("one", "two");

		var accepted = harness.Run.Write(
			harness.Stores,
			NoteScope.Machine,
			Claim(harness),
			Good() with { Links = ["two", "nowhere"] },
			["one", "two"]);

		accepted.LinksDropped.ShouldBe(["nowhere"]);

		var content = File.ReadAllText(Path.Combine(harness.Store, "one.md"));

		content.ShouldContain("[[two]]");
		content.ShouldNotContain("nowhere");
	}

	/// <summary>
	/// The anti-drift mechanism that survives a session ending: it lives in data handed over on
	/// every call rather than in a prompt that compaction may eat.
	/// </summary>
	[Test]
	public void An_accepted_enrichment_becomes_an_example_for_the_next_note()
	{
		using var harness = Create("one", "two");

		harness.Run.Write(
			harness.Stores, NoteScope.Machine, Claim(harness), Good("The first subject, explained."), ["one", "two"]);

		var next = harness.Run.Next(harness.Stores, NoteScope.Machine, 10).Note!;

		next.Exemplars.ShouldHaveSingleItem().Gist.ShouldBe("The first subject, explained.");
		next.Vocabulary.Select(topic => topic.Name).ShouldBe(["one", "two"], ignoreOrder: true);
	}

	/// <summary>An agent re-enriching a note should look at the note, not at the answer it replaces.</summary>
	[Test]
	public void A_re_enriched_note_is_handed_over_without_its_own_enrichment()
	{
		using var harness = Create("one");

		harness.Run.Write(
			harness.Stores, NoteScope.Machine, Claim(harness), Good("The previous answer."), ["one", "two"]);

		var path = Path.Combine(harness.Store, "one.md");

		File.WriteAllText(path, File.ReadAllText(path).Replace("The body", "Edited", StringComparison.Ordinal));

		var again = harness.Run.Next(harness.Stores, NoteScope.Machine, 10).Note!;

		again.Content.ShouldNotContain("The previous answer.");
		again.Content.ShouldContain("Edited");
		again.Existing!.Gist.ShouldBe("The previous answer.");
	}

	[Test]
	public void Rebuilding_puts_notes_back_without_rewriting_them()
	{
		using var harness = Create("one");

		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), ["one", "two"]);

		var path = Path.Combine(harness.Store, "one.md");
		var written = File.GetLastWriteTimeUtc(path);

		harness.Run.Rebuild(harness.Stores, NoteScope.Machine, RebuildSelection.All);

		File.GetLastWriteTimeUtc(path).ShouldBe(written);

		harness.Run.Next(harness.Stores, NoteScope.Machine, 10).Note!.Reason.ShouldBe(StaleReason.Rebuild);
	}

	[Test]
	public void Status_reports_progress_and_the_vocabulary()
	{
		using var harness = Create("one", "two");

		harness.Run.Write(harness.Stores, NoteScope.Machine, Claim(harness), Good(), ["one", "two"]);

		var status = harness.Run.Status(harness.Stores, NoteScope.Machine, includeDrift: true);

		status.Progress.Total.ShouldBe(2);
		status.Progress.Fresh.ShouldBe(1);
		status.Progress.Remaining.ShouldBe(1);
		status.Topics.Count.ShouldBe(2);
		status.Drift.ShouldNotBeNull();
		status.PromptHash.ShouldNotBeEmpty();
	}

	private static void Refuses(Harness harness, string lease, Enrichment enrichment, string expected) =>
		Should.Throw<ArgumentException>(
				() => harness.Run.Write(harness.Stores, NoteScope.Machine, lease, enrichment, ["one", "two"]))
			.Message.ShouldContain(expected);
}
