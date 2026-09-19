using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNotes.Notes.Configuration;

/// <summary>
/// <c>.dotnotes/dotnotes.json</c>, committed beside the notes it describes.
/// <para>
/// It does two jobs with one file. It names the repository, which is the step of the naming chain a
/// person controls and the answer for a remote that will not fold. And its presence is what turns
/// repository scope on: committing notes into a shared repository is the repository owner's
/// decision, so a server registered globally writes nothing into a repository that has not made it.
/// </para>
/// </summary>
public sealed record RepositoryConfigFile
{
	/// <summary>The folder holding the config and the notes, relative to the repository root.</summary>
	public const string DirectoryName = ".dotnotes";

	/// <summary>The config file's own name, inside <see cref="DirectoryName"/>.</summary>
	public const string FileName = "dotnotes.json";

	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>
	/// What this repository is called, wherever it is cloned. Null leaves the name to the remote.
	/// </summary>
	public string? Repository { get; init; }

	/// <summary>
	/// Where the notes are, relative to this file. Committed and not overridable by an argument or
	/// an environment variable: a repository store at a path that differs per machine is not a
	/// repository store, and two clones would quietly stop sharing one.
	/// </summary>
	public string Notes { get; init; } = "notes";

	/// <summary>The file this was read from. Absent means repository scope is not enabled here.</summary>
	[JsonIgnore]
	public string? Path { get; init; }

	/// <summary>The path the file would have, whether or not it is there.</summary>
	public static string PathFor(string repositoryRoot) =>
		System.IO.Path.Combine(repositoryRoot, DirectoryName, FileName);

	/// <summary>
	/// The config for a repository root, or null where there is none.
	/// <para>
	/// A file that is present and unreadable throws, where a preference file would fall back to its
	/// defaults. The difference is what the file decides: every value in a preference file has a
	/// working default, while this one chooses which repository key is used, and defaulting past a
	/// typo would file a repository's notes under a second name without saying so. Absent is still
	/// fine, and still means no repository scope.
	/// </para>
	/// </summary>
	/// <exception cref="DotNotesConfigurationException">The file is there and cannot be understood.</exception>
	public static RepositoryConfigFile? Read(string? repositoryRoot)
	{
		if (repositoryRoot is not { Length: > 0 }) return null;

		var path = PathFor(repositoryRoot);
		if (!File.Exists(path)) return null;

		try
		{
			var parsed = JsonSerializer.Deserialize<RepositoryConfigFile>(File.ReadAllText(path), Options);

			return (parsed ?? new RepositoryConfigFile()) with { Path = path };
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			throw new DotNotesConfigurationException(path, exception);
		}
	}
}
