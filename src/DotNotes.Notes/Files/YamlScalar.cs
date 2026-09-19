namespace DotNotes.Notes.Files;

/// <summary>
/// One value, rendered so YAML reads it back as the string it went in as.
/// <para>
/// Quoting only where it is needed. A note's properties are read by a person in Obsidian's
/// properties pane and in a diff, and quoting everything makes both noisier than they need to be --
/// but under-quoting silently changes a value's type, and a description reading <c>no</c> coming
/// back as <c>false</c> is the kind of wrong nothing downstream notices.
/// </para>
/// </summary>
public static class YamlScalar
{
	/// <summary>
	/// Words YAML reads as something other than text. The 1.1 set, which is wider than 1.2 and is
	/// what several readers still use, so quoting all of them is right whichever one opens the file.
	/// </summary>
	private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
	{
		"y", "n", "yes", "no", "true", "false", "on", "off", "null", "none", "~",
	};

	/// <summary>The value as it should appear after a <c>key: </c>.</summary>
	public static string Render(string? value)
	{
		if (string.IsNullOrEmpty(value)) return "\"\"";

		return NeedsQuoting(value) ? Quote(value) : value;
	}

	private static bool NeedsQuoting(string value)
	{
		if (value.Trim().Length != value.Length) return true;
		if (Reserved.Contains(value)) return true;
		if (double.TryParse(value, out _)) return true;

		// A leading indicator changes what the line is, and a colon-space or a trailing comment
		// marker ends the value early. Everything else is safe unquoted on one line.
		if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0], StringComparison.Ordinal)) return true;
		if (value.Contains(": ", StringComparison.Ordinal)) return true;
		if (value.Contains(" #", StringComparison.Ordinal)) return true;

		return value.EndsWith(':') || value.Contains('\n');
	}

	/// <summary>
	/// Double quotes, which is the only form that can carry a line break, a quote and a backslash at
	/// once. A value containing a break is folded to a space rather than escaped: a note's property
	/// is a label, and <c>\n</c> in one is unreadable in the pane a person edits it in.
	/// </summary>
	private static string Quote(string value)
	{
		var flattened = value.ReplaceLineEndings(" ").Trim();
		var escaped = flattened.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal);

		return $"\"{escaped}\"";
	}
}
