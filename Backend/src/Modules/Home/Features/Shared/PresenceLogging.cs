using Backend.Modules.Home.Domain;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.Shared;

internal static class PresenceLogging
{
    /// <summary>Reports arrive every few seconds, so only a change of the status is worth more than debug.</summary>
    public static void LogRecorded(this ILogger logger, Presence presence, PresenceStatus previousStatus)
    {
        logger.LogDebug(
            "Presence is {Status}, desk busy: {DeskIsBusy}, last seen at {LastSeenAt}.",
            presence.Status,
            presence.DeskIsBusy,
            presence.LastSeenAt);

        if (presence.Status != previousStatus)
        {
            logger.LogInformation("Presence changed from {PreviousStatus} to {Status}.", previousStatus, presence.Status);
        }
    }
}
