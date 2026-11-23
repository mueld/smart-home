using System.Text.Json;

namespace Backend.Modules.Home.Infrastructure.HomeAssistant;

/// <summary>Builds and parses the Home Assistant WebSocket messages the gateway needs. No I/O.</summary>
internal static class HomeAssistantProtocol
{
    public static byte[] Auth(string token) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type = "auth", access_token = token });

    /// <summary>A state trigger that fires when the entity changes to <c>on</c>: Home Assistant filters on its side.</summary>
    public static byte[] SubscribeTriggerStateOn(int id, string entityId) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            id,
            type = "subscribe_trigger",
            trigger = new { platform = "state", entity_id = entityId, to = "on" },
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

        state = new StateChange(
            entityId.GetString()!,
            toState.GetProperty("state").GetString() ?? "",
            toState.GetProperty("last_changed").GetDateTimeOffset().ToUniversalTime());
        return true;
    }
    public static bool IsOnState(StateChange state) => state.State == "on";
}
public sealed class AuthenticationFailedException : Exception;