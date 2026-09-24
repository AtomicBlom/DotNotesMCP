using DotNotes.Contracts;
using DotNotes.Notes.Configuration;

namespace DotNotes.Notes.Repositories;

/// <summary>
/// Which repository a directory belongs to, resolved from disk without running git.
/// <para>
/// A worktree is not an identity, and that is the defect this server exists to remove. Claude Code
/// keys its memory on the working directory, six worktrees of one repository is an ordinary week
/// rather than a corner case, and the result is a repository whose notes are in six stores that
/// cannot see each other -- with the seventh worktree starting empty. Worse, deleting a worktree
/// strands its notes for good: the path no longer matches anything, so nothing will ever resolve to
/// them again. Keying on what <c>git rev-parse --git-common-dir</c> names collapses all of them to
/// one that outlives every checkout.
/// </para>
/// <para>
/// This is an identity, not a location. It says which repository a call is about, and so where the
/// private store is; it does not say where a committed note goes, which is the working tree the call
/// came from. See <see cref="Stores.NoteStores"/>.
/// </para>
/// </summary>
public sealed record RepositoryIdentity
{
	/// <summary>The directory the resolution started from, canonicalised.</summary>
	public required string Origin { get; init; }

	public required RepositoryKind Kind { get; init; }

	/// <summary>
	/// The working tree this call is inside: a linked worktree's own root, not the repository's.
	/// Committed notes live here, because a note committed with the code belongs to the branch that
	/// is checked out and is discarded with it.
	/// </summary>
	public string? Worktree { get; init; }

	/// <summary>
	/// The main checkout, whichever worktree asked. It names the repository, which is what keeps
	/// every worktree on one machine store.
	/// </summary>
	public string? Root { get; init; }

	/// <summary>What <c>git rev-parse --git-common-dir</c> answers, reached through <c>commondir</c>.</summary>
	public string? CommonDirectory { get; init; }

	/// <summary>
	/// This checkout's own git directory, which holds its index. A linked worktree's is under the main
	/// repository's <c>worktrees</c> folder, because each worktree stages its own changes.
	/// </summary>
	public string? GitDirectory { get; init; }

	/// <summary>The origin remote folded to <c>host/path</c>, or null where there is none to fold.</summary>
	public string? Remote { get; init; }

	/// <summary>The name a person reads: the folder notes about this repository are filed under.</summary>
	public required string Name { get; init; }

	/// <summary>
	/// The name, made unique across this machine: the folder machine notes are filed under. A name
	/// from a directory carries a hash of the path, because a folder name means something only here.
	/// A name from a remote is keyed by the remote's whole path -- <c>atomicblom-rosemcp</c> -- because
	/// <c>a/tools</c> and <c>b/tools</c> are two repositories that share a last segment. A configured
	/// name is the key as written, because a person chose it.
	/// </summary>
	public required string Key { get; init; }

	public required RepositoryNameSource NamedBy { get; init; }

	/// <summary>
	/// The committed config in <see cref="Worktree"/>, or null where this checkout has not opted in.
	/// Null is why repository scope refuses: see <see cref="RepositoryConfigFile"/>.
	/// <para>
	/// Read from the checkout the caller is in rather than from <see cref="Root"/>, because opting
	/// in happens on a branch. Gating on the main checkout means creating the file the refusal just
	/// named does nothing until it merges, which leaves the person no reason to doubt they did it
	/// right.
	/// </para>
	/// </summary>
	public RepositoryConfigFile? Config { get; init; }

	/// <summary>
	/// The committed config in <see cref="Root"/>, which is the only one allowed to name the
	/// repository. A name taken from the checkout the caller is in gives one repository two machine
	/// stores the moment one branch spells it differently, and that is the fragmentation this server
	/// removes arriving by a different door. The same file as <see cref="Config"/> everywhere except
	/// a linked worktree.
	/// </summary>
	public RepositoryConfigFile? NamingConfig { get; init; }

	/// <summary>
	/// Whether this repository has a working tree to commit a note to. False for a bare repository
	/// and for a directory outside git, both of which leave machine scope working and repository
	/// scope refusing.
	/// </summary>
	public bool HasWorkingTree =>
		Kind is not (RepositoryKind.Bare or RepositoryKind.NoRepository) && Worktree is { Length: > 0 };

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

		if (layout is null) return Declared(origin) ?? Outside(origin);

		var config = RepositoryConfigFile.Read(layout.Worktree);
		var naming = NamingConfigFor(layout, config);
		var remote = RemoteName.Normalise(GitConfigFile.OriginUrl(layout.CommonDirectory));
		var (name, namedBy) = Named(layout, naming, remote);

		return new RepositoryIdentity
		{
			Origin = origin,
			Kind = layout.Kind,
			Worktree = layout.Worktree,
			Root = layout.Root,
			CommonDirectory = layout.CommonDirectory,
			GitDirectory = layout.GitDirectory,
			Remote = remote,
			Name = name,
			Key = namedBy switch
			{
				RepositoryNameSource.DirectoryName => Unique(name, layout.Root ?? origin),
				RepositoryNameSource.OriginRemote => Slug.Of(RemoteName.PathOf(remote)),
				_ => name,
			},
			NamedBy = namedBy,
			Config = config,
			NamingConfig = naming,
		};
	}

	/// <summary>
	/// The config allowed to name the repository. A linked worktree is the only shape whose checkout
	/// can hold a different answer from another checkout of the same repository, so it is the only
	/// one that reads a second file; everywhere else the checkout's own config is the repository's.
	/// </summary>
	/// <exception cref="DotNotesConfigurationException">The main checkout's config is there and malformed.</exception>
	private static RepositoryConfigFile? NamingConfigFor(GitLayout layout, RepositoryConfigFile? config) =>
		layout.Kind == RepositoryKind.LinkedWorktree ? RepositoryConfigFile.Read(layout.Root) : config;

	/// <summary>
	/// A repository git does not know about, declared by a <c>.dotnotes/dotnotes.json</c> in this
	/// directory or one above it -- or null where there is none.
	/// <para>
	/// The name is required here. Inside git the chain can fall back to a remote or a folder; out here
	/// the only thing left to key on is a hash of the path, which is exactly what moving the workspace
	/// changes, so a config that names nothing refuses and says what to add.
	/// </para>
	/// </summary>
	/// <exception cref="DotNotesConfigurationException">The config is malformed or names no repository.</exception>
	private static RepositoryIdentity? Declared(string origin)
	{
		for (var directory = new DirectoryInfo(origin); directory is not null; directory = directory.Parent)
		{
			if (RepositoryConfigFile.Read(directory.FullName) is not { } config) continue;

			if (config.Repository is not { Length: > 0 } named)
			{
				throw new DotNotesConfigurationException(
					config.Path!,
					new InvalidDataException(
						"Outside git, this file is what names the repository, so it needs "
							+ """{"repository": "<name>"}. Without one, its notes would be keyed to a path."""));
			}

			var root = CanonicalPath.Of(directory.FullName);
			var name = Slug.Of(named);

			return new RepositoryIdentity
			{
				Origin = origin,
				Kind = RepositoryKind.Configured,
				Worktree = root,
				Root = root,
				CommonDirectory = null,
				GitDirectory = null,
				Remote = null,
				Name = name,
				Key = name,
				NamedBy = RepositoryNameSource.ConfiguredName,
				Config = config,
				NamingConfig = config,
			};
		}

		return null;
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
			NamingConfig = null,
		};
	}

	/// <summary>
	/// The naming chain, in the order a later step cannot overrule an earlier one: what a person
	/// committed, then what the remote says, then what the folder is called. Every step answers for
	/// the repository rather than for the checkout, so no two worktrees can disagree.
	/// </summary>
	private static (string Name, RepositoryNameSource NamedBy) Named(
		GitLayout layout,
		RepositoryConfigFile? naming,
		string? remote)
	{
		if (naming?.Repository is { Length: > 0 } configured)
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
