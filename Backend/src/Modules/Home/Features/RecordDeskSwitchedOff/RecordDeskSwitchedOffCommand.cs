using Backend.Kernel.Abstractions;

namespace Backend.Modules.Home.Features.RecordDeskSwitchedOff;

public record RecordDeskSwitchedOffCommand(DateTimeOffset reportedAt) : IApplicationCommand;
