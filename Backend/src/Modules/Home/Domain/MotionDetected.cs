using Backend.Kernel.Domain;

namespace Backend.Modules.Home.Domain;

/// <summary>
/// The motion sensor at the entrance reported motion (state changed to <c>on</c>).
/// <paramref name="DetectedAt"/> is the time Home Assistant gives for the change, never the time of processing (AD-8).
/// Published through the mediator by the Home Assistant gateway; the slice that handles it turns it into the
/// command <c>RecordMotion</c> (AD-18, AD-19).
/// </summary>
public sealed record MotionDetectedDomainEvent(DateTimeOffset DetectedAt) : DomainEvent;
