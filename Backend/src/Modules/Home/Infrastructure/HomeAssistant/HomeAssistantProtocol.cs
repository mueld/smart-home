using System.Text.Json;

namespace Backend.Modules.Home.Infrastructure.HomeAssistant;

/// <summary>Builds and parses the Home Assistant WebSocket messages the gateway needs. No I/O.</summary>
internal static class HomeAssistantProtocol
{
    public static byte[] Auth(string token) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type = "auth", access_token = token });

    /// <summary>
    /// One state trigger for all given entities. <c>to: null</c> makes Home Assistant send state changes only, not
    /// attribute changes; the gateway filters by entity and state.
    /// </summary>
    public static byte[] SubscribeTriggerStateChanges(int id, IReadOnlyList<string> entityIds) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            id,
            type = "subscribe_trigger",
            trigger = new { platform = "state", entity_id = entityIds, to = (string?)null },
        });

    public static bool? SubscriptionConfirmed(int id, JsonElement message)
    {
        if (Id(message) != id)
            return null;
        
        return GetType(message) == "result" && Id(message) == id && IsSuccess(message);
    }

    public static bool IsAuthRequest(JsonElement message) => GetType(message) == "auth_required";

    public static void ValidateAuthResponseOrThrow(JsonElement message)
    {

        switch (GetType(message))
        {
            case "auth_ok":
                break;
            case "auth_invalid":
                throw new AuthenticationFailedException();
            default:
                throw new InvalidOperationException("Unexpected reply to authentication.");
        }
    }

    public static string? GetType(JsonElement message) =>
        message.TryGetProperty("type", out var type) ? type.GetString() : null;

    public static int? Id(JsonElement message) =>
        message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : null;

    public static bool IsSuccess(JsonElement message) =>
        message.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;

    /// <summary>What the gateway needs from a trigger event: the entity, its new state and when the state changed.</summary>
    public sealed record StateChange(string EntityId, string State, DateTimeOffset ChangedAt);

    /// <summary>
    /// Parses <c>event.variables.trigger</c> with <c>entity_id</c> and <c>to_state</c> (<c>state</c>, <c>last_changed</c>).
    /// Returns null for anything that is not such an event, for example when the entity was removed.
    /// </summary>
    public static bool TryParseTrigger(JsonElement message, out StateChange? state)
    {
        if (GetType(message) != "event"
            || !message.TryGetProperty("event", out var ev)
            || !ev.TryGetProperty("variables", out var variables)
            || !variables.TryGetProperty("trigger", out var trigger)
            || trigger.ValueKind != JsonValueKind.Object
            || !trigger.TryGetProperty("entity_id", out var entityId)
            || !trigger.TryGetProperty("to_state", out var toState)
            || toState.ValueKind != JsonValueKind.Object)
        {
            state = null;
            return false;
        }

        state = ParseState(toState) with { EntityId = entityId.GetString()! };
        return true;
    }

    /// <summary>Parses a state object, as <c>to_state</c> of a trigger event or the body of <c>GET /api/states/{entity_id}</c>.</summary>
    public static StateChange ParseState(JsonElement state) =>
        new(
            state.GetProperty("entity_id").GetString()!,
            state.GetProperty("state").GetString() ?? "",
            state.GetProperty("last_changed").GetDateTimeOffset().ToUniversalTime());
    public static bool IsOnState(StateChange state) => state.State == "on";

    public static bool IsOffState(StateChange state) => state.State == "off";

    /// <summary>Home Assistant sends sensor values as invariant-culture strings; anything else (<c>unavailable</c>) is unknown.</summary>
    public static double? Number(StateChange state) =>
        double.TryParse(state.State, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
public sealed class AuthenticationFailedException : Exception;