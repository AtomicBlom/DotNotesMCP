using DotNotes.Contracts;
using DotNotes.Index;
using DotNotes.Index.Enrichment;
using DotNotes.Index.Tools;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;

using Microsoft.Extensions.DependencyInjection;

namespace DotNotes.Server;

/// <summary>The indexing mode's registration, which shares nothing with the note tools but the store.</summary>
public static class IndexServiceCollectionExtensions
{
	/// <summary>
	/// What an indexing agent reads before it starts.
	/// <para>
	/// Longer than the note mode's instructions, and that is not a contradiction of the budget. This
	/// surface is never served beside anything else: a session that sees it is here to do one thing
	/// several hundred times, and every sentence that makes two of those consistent pays for itself
	/// immediately. The note mode's text is loaded into every session, including the ones that never
	/// write a note, which is why it is spare.
	/// </para>
	/// <para>
	/// It is also deliberately short of nuance, because its hash stales the whole corpus. Anything
	/// that might be tuned belongs in the exemplars, which are data and change freely.
	/// </para>
	/// </summary>
	public const string Instructions = """
		Enrich this note store so a coding agent can find things in it later. You are the only
		intelligence in the loop: this server holds the notes, the vocabulary and the progress, and
		makes no model calls of its own.

		The loop is three tools:

		- note_index_next claims one note and returns everything needed to enrich it: the text, the
		  topics already in use, accepted examples from this store, and the notes it could link to.
		  Do not go looking for more. What you read decides what you write, and consistency across
		  hundreds of notes is the whole value.
		- note_index_write submits the enrichment and releases the claim.
		- note_index_skip releases a note that needs none -- a stub, an index page, a paste of raw
		  output -- and records that answer so it is not asked again.

		Then call note_index_next again, until it answers drained.

		What to write:

		- gist: one sentence under 140 characters, naming the subject and what the note says about
		  it. Start with the subject, never with "This note". A reader deciding whether to open the
		  note sees this and nothing else.
		- asks: three to seven questions the note answers, phrased as somebody who has not read it
		  would ask. These are the highest-value field, because search matches them directly: a
		  question using the words a future reader will use is what makes the note findable at all.
		  Ask about what is in the note, not what it implies. Where it names a specific type, error,
		  file or person, put that name in a question.
		- topics: two to six, from the vocabulary supplied. Prefer the closest existing topic over a
		  better-fitting new one -- a topic used once is a topic nobody can filter by. Where nothing
		  fits, use a new one and list it in newTopics, which is what makes adding one deliberate.
		- entities: proper nouns a search would type verbatim -- type names, products, people, file
		  paths, error codes. Copy them exactly as the note spells them.
		- aliases: other names for the subject, including an acronym or its expansion.
		- links: notes this one is genuinely about the same thing as, only from the candidates
		  supplied. Zero is a normal answer.
		- confidence: about the note, not about your summary of it.

		Write what is there. A note that is a bare checklist gets a gist saying it is a checklist of
		those things; do not infer a purpose the note does not state, and do not fill a field by
		guessing. Short and accurate beats complete.

		The server validates shape and refuses a write that breaks it, naming what to fix -- a
		refusal is a correction, not an error to work around. It writes the result into the note's
		own frontmatter, which is what makes the enrichment visible in Obsidian and outlive this
		index. It touches no other key and not one line of the body.
		""";

	/// <summary>
	/// Registers the five indexing tools and nothing else.
	/// <para>
	/// A separate method rather than a flag on the other one, so there is no path on which both
	/// surfaces are registered. The two are never served together, and the cheapest way to keep that
	/// true is for no code to exist that could do it.
	/// </para>
	/// </summary>
	public static IMcpServerBuilder AddDotNotesIndexing(
		this IServiceCollection services,
		NoteOptions options,
		NoteScope scope)
	{
		var target = new IndexTarget { Directory = options.DefaultRoot, Scope = scope };
		var search = new CrawlingNoteSearch(options);

		services.AddSingleton(options);
		services.AddSingleton(target);
		services.AddSingleton<INoteSearch>(search);
		services.AddSingleton(new IndexRun(options, search, Instructions));

		return services
			.AddMcpServer(server =>
			{
				server.ServerInfo = new() { Name = "dotnotes-index", Version = "1" };
				server.ServerInstructions = Instructions;
			})
			.WithTools<NoteIndexTools>(ToolJson.Options)
			.WithToolErrorMessages()
			.WithRequestFilters(filters => filters.AddListToolsFilter(next => async (context, cancellationToken) =>
			{
				var result = await next(context, cancellationToken);

				foreach (var tool in result.Tools) ToolListing.Trim(tool);

				return result;
			}));
	}
}
