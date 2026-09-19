using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol;

namespace DotNotes.Server;

/// <summary>Lets a failure explain itself instead of being replaced by a shrug.</summary>
public static class ToolErrorReporting
{
	/// <summary>
	/// Forwards the real message of an exception the SDK would otherwise discard.
	/// <para>
	/// The SDK turns an exception it does not recognise into "An error occurred invoking
	/// 'note_search'." and drops the message. Everything thrown on the way here already knows what
	/// went wrong and says so -- a store on an unmounted drive names the drive, a repository with no
	/// opt-in prints the file to commit, an unknown scope lists the ones that exist -- and all of it
	/// would be discarded one frame from the caller.
	/// </para>
	/// <para>
	/// That matters more here than the message itself. A refusal a caller cannot act on is
	/// indistinguishable from the tool being broken, and an agent that cannot tell the difference
	/// goes back to reading files and does not come back.
	/// </para>
	/// <para>
	/// At the boundary rather than at each throw site, because the exception type carries meaning
	/// further in and only the wire needs the words.
	/// </para>
	/// </summary>
	public static IMcpServerBuilder WithToolErrorMessages(this IMcpServerBuilder builder) =>
		builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
		{
			try
			{
				return await next(context, cancellationToken);
			}
			catch (Exception exception) when (
				exception is not OperationCanceledException
				and not McpException
				&& !string.IsNullOrWhiteSpace(exception.Message))
			{
				throw new McpException(exception.Message, exception);
			}
		}));
}
