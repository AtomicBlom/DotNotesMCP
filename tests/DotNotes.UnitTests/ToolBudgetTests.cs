using System.Text.Json;

namespace DotNotes.UnitTests;

/// <summary>
/// What the surface costs a session, held to a budget.
/// <para>
/// The number that matters is the total. Every session pays it before a single call, including the
/// sessions that never use a note, so the question for any sentence is whether removing it changes
/// what the agent does. The per-tool ceilings below are guard rails against one tool growing a
/// kilobyte; the total is the assertion.
/// </para>
/// </summary>
public sealed class ToolBudgetTests
{
	/// <summary>
	/// Everything a model is shown: the instructions, the descriptions and the input schemas.
	/// Generous for three tools, and deliberately not set to today's figure -- a budget that fails on
	/// the next sentence anybody writes gets raised without being thought about.
	/// </summary>
	private const int Total = 8000;

	/// <summary>
	/// The ceiling on one description. A tool needs the claim, the alternative it beats, and what it
	/// will not do; past this it is explaining itself to somebody who has already chosen it.
	/// </summary>
	private const int PerTool = 600;

	/// <summary>
	/// The floor. Below this a description cannot name what the caller would otherwise have done,
	/// which is the one thing it is for -- and a tool that loses to reading files costs far more
	/// than the characters saved.
	/// </summary>
	private const int Minimum = 120;

	/// <summary>The ceiling on one argument's help.</summary>
	private const int PerArgument = 250;

	/// <summary>
	/// The instructions are read before the model decides how to approach anything, so they are
	/// worth more per character than anything else here -- and are still the easiest place to spend
	/// a thousand characters nobody acts on.
	/// </summary>
	private const int InstructionsCeiling = 2000;

	[Test]
	public void The_whole_model_facing_surface_stays_within_its_budget()
	{
		var measured = Surface.Instructions().Length
			+ Surface.Listed().Sum(tool =>
				(tool.Description?.Length ?? 0) + tool.InputSchema.GetRawText().Length);

		measured.ShouldBeGreaterThan(0);
		measured.ShouldBeLessThanOrEqualTo(Total, $"the surface measures {measured} characters");
	}

	[Test]
	public void No_description_is_longer_than_its_ceiling_or_shorter_than_its_floor()
	{
		foreach (var tool in Surface.Listed())
		{
			var length = tool.Description?.Length ?? 0;

			length.ShouldBeLessThanOrEqualTo(PerTool, $"{tool.Name} is {length} characters");
			length.ShouldBeGreaterThan(Minimum, $"{tool.Name} is {length} characters");
		}
	}

	[Test]
	public void No_argument_help_is_longer_than_its_ceiling()
	{
		foreach (var tool in Surface.Listed())
		{
			foreach (var (name, length) in Arguments(tool))
			{
				length.ShouldBeLessThanOrEqualTo(PerArgument, $"{tool.Name}'s {name} is {length}");
			}
		}
	}

	[Test]
	public void The_instructions_stay_within_their_budget()
	{
		var instructions = Surface.Instructions();

		instructions.Length.ShouldBeGreaterThan(0);
		instructions.Length.ShouldBeLessThanOrEqualTo(InstructionsCeiling);
	}

	/// <summary>Each argument's name and how long its help is, from the schema as it is sent.</summary>
	private static IEnumerable<(string Name, int Length)> Arguments(ModelContextProtocol.Protocol.Tool tool)
	{
		if (!tool.InputSchema.TryGetProperty("properties", out var properties)) yield break;

		foreach (var property in properties.EnumerateObject())
		{
			if (property.Value.TryGetProperty("description", out var described)
				&& described.ValueKind == JsonValueKind.String)
			{
				yield return (property.Name, described.GetString()?.Length ?? 0);
			}
		}
	}
}
