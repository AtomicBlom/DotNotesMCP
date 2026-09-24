using DotNotes.Notes.Repositories;

namespace DotNotes.UnitTests;

/// <summary>
/// That every spelling of one remote folds to one string.
/// <para>
/// Folding wrong is not cosmetic. Two clones of one repository that disagree here file their notes
/// under two names, which is the fragmentation this server removes, arriving by a different door.
/// </para>
/// </summary>
public sealed class RemoteNameTests
{
	[Test]
	[Arguments("https://github.com/AtomicBlom/RoseMCP.git")]
	[Arguments("https://github.com/AtomicBlom/RoseMCP")]
	[Arguments("http://github.com/AtomicBlom/RoseMCP.git")]
	[Arguments("git://github.com/AtomicBlom/RoseMCP")]
	[Arguments("git@github.com:AtomicBlom/RoseMCP.git")]
	[Arguments("git@github.com:AtomicBlom/RoseMCP")]
	[Arguments("ssh://git@github.com:22/AtomicBlom/RoseMCP.git")]
	[Arguments("ssh://git@github.com/AtomicBlom/RoseMCP.git")]
	[Arguments("https://github.com/AtomicBlom/RoseMCP.git/")]
	public void Every_spelling_of_one_remote_folds_together(string url) =>
		RemoteName.Normalise(url).ShouldBe("github.com/atomicblom/rosemcp");

	/// <summary>
	/// A token in the URL is a credential, not an identity: the same clone re-authenticated would
	/// otherwise become a different repository.
	/// </summary>
	[Test]
	public void Credentials_are_not_part_of_the_identity() =>
		RemoteName.Normalise("https://x-access-token:ghp_secret@github.com/AtomicBlom/RoseMCP.git")
			.ShouldBe("github.com/atomicblom/rosemcp");

	/// <summary>Hosts other than GitHub fold the same way, including a deeper path.</summary>
	[Test]
	[Arguments("https://dev.azure.com/contoso/Platform/_git/Primary", "dev.azure.com/contoso/platform/_git/primary")]
	[Arguments("https://gitlab.com/group/sub/project.git", "gitlab.com/group/sub/project")]
	[Arguments("git@bitbucket.org:team/repo.git", "bitbucket.org/team/repo")]
	public void Any_host_folds(string url, string expected) => RemoteName.Normalise(url).ShouldBe(expected);

	/// <summary>
	/// A path names a place on one machine, so two clones from it are not two clones of a shared
	/// thing. A Windows drive is the case that matters: D:\mirrors\x.git reads exactly like the
	/// host:path form an ssh remote uses, and taking D for a host would key a repository on a letter.
	/// </summary>
	[Test]
	[Arguments(@"D:\mirrors\rose.git")]
	[Arguments("D:/mirrors/rose.git")]
	[Arguments("/srv/git/rose.git")]
	[Arguments(@"\\server\share\rose.git")]
	[Arguments("file:///srv/git/rose.git")]
	[Arguments("")]
	[Arguments(null)]
	public void A_local_path_names_no_shared_identity(string? url) => RemoteName.Normalise(url).ShouldBeNull();

	[Test]
	[Arguments("github.com/atomicblom/rosemcp", "rosemcp")]
	[Arguments("dev.azure.com/contoso/platform/_git/primary", "primary")]
	[Arguments(null, null)]
	public void The_last_segment_is_what_a_repository_is_called(string? normalised, string? expected) =>
		RemoteName.LastSegment(normalised).ShouldBe(expected);

	/// <summary>The path after the host is what a remote-named repository is keyed by, however deep it goes.</summary>
	[Test]
	[Arguments("github.com/atomicblom/rosemcp", "atomicblom/rosemcp")]
	[Arguments("gitlab.com/group/sub/project", "group/sub/project")]
	[Arguments("github.com", null)]
	[Arguments(null, null)]
	public void The_path_is_everything_after_the_host(string? normalised, string? expected) =>
		RemoteName.PathOf(normalised).ShouldBe(expected);
}
