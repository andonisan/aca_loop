using System.Text.Json;
using System.Text.Json.Serialization;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>
/// The queue's wire format for a <see cref="CanonicalMessage"/>.
/// <para>
/// In contracts rather than beside the Azure queue implementation because there is now more than one
/// writer — the receiver's webhook path and the portal's manual-dispatch verb — and exactly one reader.
/// Two independent serializations would drift on something small, like whether an enum travels as a name
/// or an ordinal, and the failure would be a message the agent cannot deserialize: discovered in
/// production, on the dispatch path, as a poison message rather than an error anyone can read.
/// </para>
/// </summary>
public static class QueueMessage
{
    /// <summary>Enums as names, not ordinals. The wire form has to survive a vendor being added to the
    /// middle of the enum, which reordering ordinals would silently break.</summary>
    static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(CanonicalMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static CanonicalMessage? Deserialize(string body) =>
        JsonSerializer.Deserialize<CanonicalMessage>(body, Options);
}
