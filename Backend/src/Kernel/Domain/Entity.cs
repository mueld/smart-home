namespace Backend.Kernel.Domain;

public class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
