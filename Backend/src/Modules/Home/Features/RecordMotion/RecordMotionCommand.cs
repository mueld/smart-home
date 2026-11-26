using Backend.Kernel.Abstractions;

namespace Backend.Modules.Home.Features.RecordMotion;

public record RecordMotionCommand(DateTimeOffset detectedAt) : IApplicationCommand;
