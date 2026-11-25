using Backend.Kernel.Domain;

namespace Backend.Modules.Home.Domain;


public sealed record UserIPCheckedAtHomeDomainEvent( bool reachable, DateTimeOffset checkedAt) : DomainEvent;
