using DotNotes.Contracts;
using DotNotes.Notes.Configuration;

namespace DotNotes.Notes.Repositories;

/// <summary>
/// Which repository a directory belongs to, resolved from disk without running git.
/// <para>
/// A worktree is not an identity, and that is the defect this server exists to remove. Claude Code
/// keys its memory on the working directory, six worktrees of one repository is an ordinary week
/// rather than a corner case, and the result is a repository whose notes are in six stores that
/// cannot see each other -- with the seventh worktree starting empty. Keying on what
/// <c>git rev-parse --git-common-dir</c> names collapses all of them to one.
/// </para>
/// </summary>
public sealed record RepositoryIdentity
{
	/// <summary>The directory the resolution started from, canonicalised.</summary>
	public required string Origin { get; init; }

	public required RepositoryKind Kind { get; init; }

	/// <summary>The working tree this call is inside: a linked worktree's own root, not the repository's.</summary>
	public string? Worktree { get; init; }

	/// <summary>The one working tree notes are keyed to. The main checkout, whichever worktree asked.</summary>
	public string? Root { get; init; }

	/// <summary>What <c>git rev-parse --git-common-dir</c> answers, reached through <c>commondir</c>.</summary>
	public string? CommonDirectory { get; init; }

	/// <summary>The origin remote folded to <c>host/path</c>, or null where there is none to fold.</summary>
	public string? Remote { get; init; }

	/// <summary>The name a person reads: the folder notes about this repository are filed under.</summary>
	public required string Name { get; init; }

	/// <summary>
	/// The name, made unique across this machine. Equal to <see cref="Name"/> unless the name came
	/// from a directory, in which case it carries a hash of the path -- a folder name means
	/// something only here, and two unrelated directories called <c>tools</c> must not share a store.
	/// </summary>
	public required string Key { get; init; }

	public required RepositoryNameSource NamedBy { get; init; }

	/// <summary>
	/// The committed config, or null where the repository has not opted in. Null is why repository
	/// scope refuses: see <see cref="RepositoryConfigFile"/>.
	/// </summary>
	public RepositoryConfigFile? Config { get; init; }

	/// <summary>
	/// Whether this repository has a working tree to commit a note to. False for a bare repository
	/// and for a directory outside git, both of which leave machine scope working and repository
	/// scope refusing.
	/// </summary>
	public bool HasWorkingTree => Root is { Length: > 0 };

	/// <summary>
	/// The identity of the directory a call came from. Pure over disk: no process is started, and at
	/// most four small files are read.
	/// <para>
	/// Answered fresh every time, deliberately. The two things this reads are exactly the two a
	/// person changes while a session is open -- <c>git init</c> in a directory that was not a
	/// repository, and the committed config that opts one in to repository scope. Remembering
	/// either answer means the server keeps giving the old one, and for the config that is worse
	/// than merely stale: the refusal names the file to create, so following the instruction the
	/// tool just gave you appears to do nothing.
	/// </para>
	/// <para>
	/// Measured at 190 microseconds inside a repository and 291 outside one, against a store crawl
	/// of tens of milliseconds and one or two resolutions per call. A cache saved 0.3 ms per request
	/// and cost an answer that could be wrong about the only two things it reports.
	/// </para>
	/// </summary>
	/// <exception cref="DotNotesConfigurationException">A committed config is there and malformed.</exception>
	public static RepositoryIdentity For(string startDirectory) => Resolve(CanonicalPath.Of(startDirectory));

	private static RepositoryIdentity Resolve(string origin)
	{
		var layout = GitLayout.Find(origin);

		if (layout is null) return Outside(origin);

		var config = RepositoryConfigFile.Read(layout.Root);
		var remote = RemoteName.Normalise(GitConfigFile.OriginUrl(layout.CommonDirectory));
		var (name, namedBy) = Named(layout, config, remote);

		return new RepositoryIdentity
		{
			Origin = origin,
			Kind = layout.Kind,
			Worktree = layout.Worktree,
			Root = layout.Root,
			CommonDirectory = layout.CommonDirectory,
			Remote = remote,
			Name = name,
			Key = namedBy == RepositoryNameSource.DirectoryName ? Unique(name, layout.Root ?? origin) : name,
			NamedBy = namedBy,
			Config = config,
		};
	}

	/// <summary>
	/// A directory with no git above it. It still gets a name, because machine-scope notes are filed
	/// under one and a scratch directory is somewhere people work.
	/// </summary>
	private static RepositoryIdentity Outside(string origin)
	{
		var name = Slug.Of(Path.GetFileName(origin));

		return new RepositoryIdentity
		{
			Origin = origin,
			Kind = RepositoryKind.NoRepository,
			Worktree = null,
			Root = null,
			CommonDirectory = null,
			Remote = null,
			Name = name,
			Key = Unique(name, origin),
			NamedBy = RepositoryNameSource.DirectoryName,
			Config = null,
		};
	}

	/// <summary>
	/// The naming chain, in the order a later step cannot overrule an earlier one: what a person
	/// committed, then what the remote says, then what the folder is called.
	/// </summary>
	private static (string Name, RepositoryNameSource NamedBy) Named(
		GitLayout layout,
		RepositoryConfigFile? config,
		string? remote)
	{
		if (config?.Repository is { Length: > 0 } configured)
		{
			return (Slug.Of(configured), RepositoryNameSource.ConfiguredName);
		}

		if (RemoteName.LastSegment(remote) is { Length: > 0 } segment)
		{
			return (Slug.Of(segment), RepositoryNameSource.OriginRemote);
		}

		var directory = Path.GetFileName(layout.Root ?? layout.Worktree);

		return (Slug.Of(directory), RepositoryNameSource.DirectoryName);
	}

	/// <summary>A name that means something only on this machine, made unique on it.</summary>
	private static string Unique(string name, string path) => $"{name}-{CanonicalPath.Hash(path)}";
}
