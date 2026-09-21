using DotNotes.Contracts;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;

namespace DotNotes.Notes.Stores;

/// <summary>
/// Both stores for one directory: where each is, and whether each can be used.
/// <para>
/// Resolution reads; it never creates. Asking where the stores are is what a diagnostic does, and a
/// diagnostic that leaves a directory behind in every repository it is pointed at is a worse
/// diagnostic. Directories appear at the first write, through <see cref="NoteStore.Ensure"/>.
/// </para>
/// </summary>
public sealed record NoteStores
{
	/// <summary>The one place <c>DOTNOTES_STORE</c> is read.</summary>
	public const string StoreVariable = "DOTNOTES_STORE";

	/// <summary>Where notes with no repository go, inside the machine store.</summary>
	public const string UserFolder = "_user";

	/// <summary>The repository this was resolved for.</summary>
	public required RepositoryIdentity Repository { get; init; }

	/// <summary>The private store, which works everywhere.</summary>
	public required NoteStore Machine { get; init; }

	/// <summary>The committed store, which works where a repository has opted in.</summary>
	public required NoteStore Repo { get; init; }

	/// <summary>The machine store's root, above the per-repository folders.</summary>
	public required string MachineRoot { get; init; }

	/// <summary>Which layer named <see cref="MachineRoot"/>.</summary>
	public required MachineStoreSource MachineRootSource { get; init; }

	/// <summary>What this machine calls itself in a note's <c>machines</c> list.</summary>
	public required string MachineName { get; init; }

	/// <summary>Both stores for the directory a call came from.</summary>
	/// <exception cref="DotNotesConfigurationException">A configuration file is there and malformed.</exception>
	public static NoteStores For(string directory, NoteOptions options)
	{
		var identity = RepositoryIdentity.For(directory);
		var settings = MachineSettingsFile.Read(options.LocalAppData);
		var (root, source) = MachineRootFrom(options, settings);

		return new NoteStores
		{
			Repository = identity,
			MachineRoot = root,
			MachineRootSource = source,
			MachineName = Configuration.MachineName.Of(settings, options),
			Machine = MachineStore(identity, root, source),
			Repo = RepositoryStore(identity),
		};
	}

	/// <summary>The store a scope names, whether or not it can be used.</summary>
	public NoteStore this[NoteScope scope] => scope == NoteScope.Repository ? Repo : Machine;

	/// <summary>
	/// The machine store's root, and which layer supplied it. Precedence is argument, environment,
	/// settings, default -- narrowest intent first, and the default last because it is the only one
	/// nobody chose.
	/// </summary>
	private static (string Root, MachineStoreSource Source) MachineRootFrom(
		NoteOptions options,
		MachineSettingsFile settings)
	{
		if (options.MachineStore is { Length: > 0 } argument)
		{
			return (CanonicalPath.Of(argument), MachineStoreSource.Argument);
		}

		if (options.Environment(StoreVariable) is { Length: > 0 } environment)
		{
			return (CanonicalPath.Of(environment), MachineStoreSource.Environment);
		}

		if (settings.MachineStore is { Length: > 0 } configured)
		{
			return (CanonicalPath.Of(configured), MachineStoreSource.Settings);
		}

		return (
			Path.Combine(MachineSettingsFile.DirectoryFor(options.LocalAppData), "notes"),
			MachineStoreSource.Default);
	}

	/// <summary>
	/// The private store for this repository.
	/// <para>
	/// A store somebody chose must already be reachable; only the default is created on demand. The
	/// case this exists for is a vault on a drive that is not mounted, where falling back to the
	/// default would write notes to a second place nobody is looking at -- which is the
	/// fragmentation this server removes, arriving by a different door.
	/// </para>
	/// </summary>
	private static NoteStore MachineStore(
		RepositoryIdentity identity,
		string root,
		MachineStoreSource source)
	{
		// A directory outside git is not a durable thing to key notes to -- its key is a hash of a
		// path that may well change -- so its notes are about the machine rather than about it.
		var folder = identity.Kind == RepositoryKind.NoRepository ? UserFolder : identity.Key;
		var path = Path.Combine(root, folder);

		if (source == MachineStoreSource.Default) return NoteStore.Available(NoteScope.Machine, path);

		if (!Directory.Exists(root) && !Directory.Exists(Path.GetDirectoryName(root) ?? root))
		{
			return NoteStore.Refused(
				NoteScope.Machine,
				path,
				$"The machine store at {root} cannot be reached, and it was chosen rather than "
					+ $"defaulted ({Describe(source)}), so nothing is written elsewhere. Mount it, or "
					+ "point it somewhere that exists.");
		}

		return NoteStore.Available(NoteScope.Machine, path);
	}

	/// <summary>
	/// The committed store, which exists only where the checkout has opted in.
	/// <para>
	/// It lives in the working tree the call came from, not in the main checkout. A committed note
	/// is a tracked file: it belongs to the branch that learned the fact, is reviewed with the
	/// change it describes, and reaches the other worktrees the way every other tracked file does.
	/// Writing it into the main checkout instead puts it on whatever branch that happens to have out
	/// and dirties a tree nobody in the session is looking at. Sharing before a merge is what the
	/// machine store is for, and the machine store is keyed to the repository precisely so it
	/// survives this worktree being discarded.
	/// </para>
	/// <para>
	/// Committing notes into a shared repository is the repository owner's decision, not one an
	/// agent makes on first contact: without the gate, a server registered once and used everywhere
	/// drops an untracked folder into whichever repository happened to be open.
	/// </para>
	/// </summary>
	private static NoteStore RepositoryStore(RepositoryIdentity identity)
	{
		if (!identity.HasWorkingTree)
		{
			var because = identity.Kind == RepositoryKind.Bare
				? "A bare repository has no working tree, so there is nothing to commit a note to. Use machine scope."
				: $"{identity.Origin} is not in a git repository, so there is nothing to commit a note to. "
					+ "Run git init, or use machine scope.";

			return NoteStore.Refused(NoteScope.Repository, string.Empty, because);
		}

		var worktree = identity.Worktree!;

		if (identity.Config is not { } config)
		{
			return NoteStore.Refused(
				NoteScope.Repository,
				Path.Combine(worktree, RepositoryConfigFile.DirectoryName, "notes"),
				$"This checkout has not opted in to committed notes. Create "
					+ $"{RepositoryConfigFile.PathFor(worktree)} containing "
					+ $$"""{"repository": "{{identity.Name}}"}""" + " and commit it. Until then, use machine scope.");
		}

		// Relative to the config file, so a repository that wants its notes somewhere else says so
		// once and every clone agrees. Nothing overrides it per machine: a repository store at a
		// path that differs per machine is not a repository store. The config is this checkout's, so
		// the path this resolves to is inside this working tree.
		var configured = Path.Combine(Path.GetDirectoryName(config.Path!)!, config.Notes);

		return NoteStore.Available(NoteScope.Repository, CanonicalPath.Of(configured));
	}

	private static string Describe(MachineStoreSource source) => source switch
	{
		MachineStoreSource.Argument => "--store",
		MachineStoreSource.Environment => StoreVariable,
		MachineStoreSource.Settings => "machineStore in settings.json",
		_ => "the default",
	};
}
