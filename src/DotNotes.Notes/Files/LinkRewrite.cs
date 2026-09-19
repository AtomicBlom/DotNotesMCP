using DotNotes.Contracts;

namespace DotNotes.Notes.Files;

/// <summary>
/// Pointing a note's links at a note that has moved.
/// <para>
/// A rename that leaves the links behind is worse than no rename: every note that referenced the
/// old name now points at nothing, and Obsidian renders each of those as an invitation to create a
/// note that already exists under another name. Rewriting them is what makes renaming safe enough
/// to do.
/// </para>
/// </summary>
public static class LinkRewrite
{
	/// <summary>
	/// A body with every link to <paramref name="oldName"/> pointed at the note's new name and
	/// store, and how many were changed.
	/// <para>
	/// The prefix is recomputed rather than kept, because a move between stores changes what the
	/// link has to say. A machine note linking to another machine note writes <c>[[name]]</c>; once
	/// that note is committed, the same link has to become <c>[[repo:name]]</c> or it resolves to
	/// nothing. An alias survives -- it is what the author wanted the link to read as, and the move
	/// has nothing to say about that.
	/// </para>
	/// </summary>
	/// <param name="body">The linking note's prose.</param>
	/// <param name="oldName">The name being moved away from.</param>
	/// <param name="newName">What the note is called now.</param>
	/// <param name="target">The store the note is in now.</param>
	/// <param name="from">The store the linking note is in.</param>
	public static (string Body, int Rewritten) Retarget(
		string body,
		string oldName,
		string newName,
		NoteScope target,
		NoteScope from)
	{
		var links = Wikilink.In(body);
		var rewritten = 0;
		var result = body;

		// Right to left, so replacing one link does not move the offsets of the ones before it.
		foreach (var link in links.OrderByDescending(link => link.Start))
		{
			if (!Names(link.Target, oldName)) continue;

			var replacement = Wikilink.Write(newName, target == from ? null : target, link.Alias);

			result = result[..link.Start] + replacement + result[(link.Start + link.Length)..];
			rewritten++;
		}

		return (result, rewritten);
	}

	/// <summary>
	/// Whether a link's target names a note, ignoring any folder it was qualified by. Obsidian
	/// resolves <c>[[rosemcp/one]]</c> and <c>[[one]]</c> to the same note, so a rename has to catch
	/// both or it fixes half the links and reports success.
	/// </summary>
	public static bool Names(string target, string name) =>
		Bare(target).Equals(name, StringComparison.OrdinalIgnoreCase);

	/// <summary>A link target without the folder that qualified it.</summary>
	public static string Bare(string target) => target[(target.LastIndexOf('/') + 1)..];
}
