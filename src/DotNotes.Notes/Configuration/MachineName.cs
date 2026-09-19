using DotNotes.Notes.Repositories;

namespace DotNotes.Notes.Configuration;

/// <summary>
/// What this machine is called in a note's <c>machines</c> list.
/// <para>
/// It exists because the machine store may be pointed at a vault that syncs between two machines,
/// and then "machine scope" no longer means one box. A note about an ARM64 quirk says which box it
/// is about, and search surfaces one pinned elsewhere with a flag rather than hiding it -- the
/// other machine's quirk is often exactly what is being looked for.
/// </para>
/// </summary>
public static class MachineName
{
	/// <summary>The one place <c>DOTNOTES_MACHINE_NAME</c> is read.</summary>
	public const string Variable = "DOTNOTES_MACHINE_NAME";

	/// <summary>
	/// The chosen name, or the host's own. Slugged, because it is a value in frontmatter a person
	/// reads and a term a search matches.
	/// </summary>
	public static string Of(MachineSettingsFile settings)
	{
		if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } fromEnvironment)
		{
			return Slug.Of(fromEnvironment);
		}

		return Slug.Of(settings.MachineName is { Length: > 0 } configured
			? configured
			: Environment.MachineName);
	}
}
