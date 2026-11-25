namespace Backend.Modules.Home.Domain;

/// <summary>The desk is in use above this power; an unknown power does not count as use.</summary>
public sealed record DeskBusyThreshold(double Watts)
{
    public bool IsBusy(double? power) => power > Watts;
}
