using Backend.Kernel.Abstractions;

namespace Backend.Modules.Home.Features.RecordIpPresence;

public record RecordIpPresenceCommand(bool reachable, DateTimeOffset checkedAt):IApplicationCommand;
