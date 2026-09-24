namespace DotNotes.Contracts;

/// <summary>
/// Which step of the naming chain named a repository. Reported on the answer, because a name that
/// surprises its reader is otherwise something they reverse-engineer from the filesystem.
/// </summary>
public enum RepositoryNameSource
{
	/// <summary>
	/// The <c>repository</c> field of a committed <c>.dotnotes/dotnotes.json</c>. The one answer a
	/// person chose: it survives a remote being renamed, and it is what makes two clones on two
	/// machines agree when their remotes are spelled differently enough not to fold together.
	/// </summary>
	ConfiguredName,

	/// <summary>
	/// The origin remote, folded: named by its last segment, keyed by its whole path, so a fork and
	/// its upstream, or two owners' repositories of one name, keep two stores.
	/// </summary>
	OriginRemote,

	/// <summary>
	/// The repository root's own folder name. Meaningful only on this machine, so a name from here
	/// carries a hash of the path: two unrelated directories called <c>tools</c> must not merge.
	/// </summary>
	DirectoryName,
}
