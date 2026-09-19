using System.Text.RegularExpressions;

using DotNotes.Contracts;

namespace DotNotes.Notes.Files;

/// <summary>
/// A link from one note to another, in the form Obsidian resolves.
/// <para>
/// Three spellings, because there are two stores. A bare target is looked for beside the note and
/// then across its own store; a target carrying a folder is exact; and a target prefixed
/// <c>machine:</c> or <c>repo:</c> names the other store, which Obsidian shows as unresolved -- and
/// that is honest, because the note genuinely is not in that vault.
/// </para>
/// </summary>
public sealed partial record Wikilink
{
	/// <summary>The prefix naming a note in the private store.</summary>
	public const string MachinePrefix = "machine:";

	/// <summary>The prefix naming a note in the committed store.</summary>
	public const string RepositoryPrefix = "repo:";

	/// <summary>The target as written, without any store prefix and without the alias.</summary>
	public required string Target { get; init; }

	/// <summary>The store the target names, or null where the link does not say.</summary>
	public NoteScope? Scope { get; init; }

	/// <summary>The text shown in place of the target, where the link carries one.</summary>
	public string? Alias { get; init; }

	/// <summary>Where the link starts in the body, so a rename can rewrite exactly it.</summary>
	public required int Start { get; init; }

	/// <summary>How many characters the whole <c>[[...]]</c> covers.</summary>
	public required int Length { get; init; }

	/// <summary>The link as it appears in the note.</summary>
	public string Rendered => Write(Target, Scope, Alias);

	/// <summary>
	/// Every link in a note's body, in order.
	/// <para>
	/// Code is skipped. A fenced block full of <c>[[</c> is sample text or a nested array, and
	/// counting it produces dangling links nobody wrote and backlinks between notes that do not
	/// mention each other.
	/// </para>
	/// </summary>
	public static IReadOnlyList<Wikilink> In(string body)
	{
		var found = new List<Wikilink>();
		var skip = Code.Spans(body);

		foreach (Match match in Pattern().Matches(body))
		{
			if (skip.Any(span => match.Index >= span.Start && match.Index < span.End)) continue;

			var inner = match.Groups[1].Value;
			var pipe = inner.IndexOf('|', StringComparison.Ordinal);
			var target = (pipe >= 0 ? inner[..pipe] : inner).Trim();
			var alias = pipe >= 0 ? inner[(pipe + 1)..].Trim() : null;

			if (target.Length == 0) continue;

			var (scope, bare) = Unprefixed(target);

			found.Add(new Wikilink
			{
				Target = bare,
				Scope = scope,
				Alias = alias is { Length: > 0 } ? alias : null,
				Start = match.Index,
				Length = match.Length,
			});
		}

		return found;
	}

	/// <summary>A link, written the way this server writes one.</summary>
	public static string Write(string target, NoteScope? scope, string? alias)
	{
		var prefix = scope switch
		{
			NoteScope.Machine => MachinePrefix,
			NoteScope.Repository => RepositoryPrefix,
			_ => string.Empty,
		};

		return alias is { Length: > 0 } ? $"[[{prefix}{target}|{alias}]]" : $"[[{prefix}{target}]]";
	}

	/// <summary>The store a target names, and the target without that prefix.</summary>
	private static (NoteScope? Scope, string Target) Unprefixed(string target)
	{
		if (target.StartsWith(MachinePrefix, StringComparison.OrdinalIgnoreCase))
		{
			return (NoteScope.Machine, target[MachinePrefix.Length..].Trim());
		}

		if (target.StartsWith(RepositoryPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return (NoteScope.Repository, target[RepositoryPrefix.Length..].Trim());
		}

		return (null, target);
	}

	/// <summary>A target with no line break in it, which is what Obsidian will follow.</summary>
	[GeneratedRegex(@"\[\[([^\[\]\r\n]+)\]\]", RegexOptions.Compiled)]
	private static partial Regex Pattern();
}
