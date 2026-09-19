using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNotes.Notes.Configuration;

/// <summary>
/// What this machine has chosen, at <c>%LOCALAPPDATA%/BinaryVibrance/DotNotes/settings.json</c>.
/// <para>
/// Beside the store it points at rather than inside it, because it is the file that says where the
/// store is. Per machine and never committed: the x64 box and the ARM64 box are entitled to keep
/// their notes in different places, and one of those places may be a drive that is not always
/// mounted.
/// </para>
/// </summary>
public sealed record MachineSettingsFile
{
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		WriteIndented = true,
	};

	/// <summary>
	/// Where the machine store is, if not the default. An Obsidian vault is the point of this
	/// setting: pointed there, the notes are files the person reads and edits beside the agent.
	/// </summary>
	public string? MachineStore { get; init; }

	/// <summary>
	/// What this machine is called in a note's <c>machines</c> list. Defaults to the host name,
	/// and is settable because a host name is not always the name a person uses for the box.
	/// </summary>
	public string? MachineName { get; init; }

	/// <summary>The file this was read from, or where it would be.</summary>
	[JsonIgnore]
	public string Path { get; init; } = string.Empty;

	/// <summary>The vendor and product folder the settings, the locks and the default store share.</summary>
	/// <param name="localAppData">Where local application data is, for a test that wants its own.</param>
	public static string DirectoryFor(string? localAppData = null)
	{
		var configured = localAppData is { Length: > 0 }
			? localAppData
			: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		var root = configured.Length > 0 ? configured : System.IO.Path.GetTempPath();

		return System.IO.Path.Combine(root, "BinaryVibrance", "DotNotes");
	}

	/// <summary>The settings file, which need not exist.</summary>
	public static string PathFor(string? localAppData = null) =>
		System.IO.Path.Combine(DirectoryFor(localAppData), "settings.json");

	/// <summary>
	/// What has been chosen, or the defaults.
	/// <para>
	/// A missing file is the defaults and is entirely normal. A file that is there and cannot be
	/// parsed throws, which is where this parts company with a preference file: every value in a
	/// preference file has a working default, while this one says where notes are written, and
	/// defaulting past a typo would put them somewhere the person is not looking and say nothing.
	/// </para>
	/// </summary>
	/// <exception cref="DotNotesConfigurationException">The file is there and cannot be understood.</exception>
	public static MachineSettingsFile Read(string? localAppData = null)
	{
		var path = PathFor(localAppData);

		if (!File.Exists(path)) return new MachineSettingsFile { Path = path };

		try
		{
			var parsed = JsonSerializer.Deserialize<MachineSettingsFile>(File.ReadAllText(path), Options);

			return (parsed ?? new MachineSettingsFile()) with { Path = path };
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			throw new DotNotesConfigurationException(path, exception);
		}
	}

	/// <summary>
	/// Writes the whole file, reporting whether it landed rather than throwing. Nothing in this
	/// server needs to write it today; it exists so the one place that formats these settings is
	/// the one place that reads them.
	/// </summary>
	public static bool Write(MachineSettingsFile settings, string? localAppData = null)
	{
		try
		{
			var path = PathFor(localAppData);

			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));

			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
