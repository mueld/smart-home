using System.Data.Common;

namespace Backend.Kernel.Persistence;

/// <summary>
/// Scoped state of the running transaction (AD-14): which transaction is open, which DbContexts
/// are currently executing a command, and a counter for savepoint names.
/// While domain events are dispatched a new frame is opened: a command sent from an event handler is not a
/// re-entry into the command that raised the event (it runs after that command's SaveChanges completed).
/// </summary>
public sealed class TransactionCoordinator
{
    private readonly Stack<HashSet<Type>> _frames = new([[]]);
    private int _savepoints;

    public DbTransaction? Transaction { get; set; }

    public bool TryEnter(Type dbContextType) => _frames.Peek().Add(dbContextType);

    public void Exit(Type dbContextType) => _frames.Peek().Remove(dbContextType);

    public string NextSavepointName() => $"kernel_sp_{++_savepoints}";

    public IDisposable BeginEventDispatch()
    {
        _frames.Push([]);
        return new Frame(this);
    }

    private sealed class Frame(TransactionCoordinator owner) : IDisposable
    {
        public void Dispose() => owner._frames.Pop();
    }
}
