using Backend.Kernel.Abstractions;

namespace Backend.Modules.Home.Features.RecordPower;

public record RecordPowerCommand(double? watts, DateTimeOffset reportedAt) : IApplicationCommand;
