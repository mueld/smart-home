using Backend.Kernel.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Kernel.Behaviors;

/// <summary>
/// Second step for commands (AD-5, AD-14). Resolves the DbContext of the module that owns the command.
/// The outermost command starts the transaction and is the only one that commits (on success) or rolls back
/// (on a failed outcome or an exception). A command from another module joins the transaction over the shared
/// connection and sets a savepoint, to which it rolls back on failure. A DbContext that is already executing
/// a command in this scope is not entered again.
/// </summary>
public sealed class TransactionBehavior<TMessage, TResponse>(
    IServiceProvider services,
    ModuleRegistry modules,
    TransactionCoordinator coordinator)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IOutcome<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var contextType = modules.ResolveDbContext(typeof(TMessage));
        var context = (DbContext)services.GetRequiredService(contextType);

        if (!coordinator.TryEnter(contextType))
        {
            throw new InvalidOperationException(
                $"{contextType.Name} is already executing a command in this scope. {typeof(TMessage).Name} cannot enter it again (AD-14).");
        }

        try
        {
            return coordinator.Transaction is null
                ? await RunOutermost(context, message, next, cancellationToken)
                : await RunJoined(context, coordinator.Transaction, message, next, cancellationToken);
        }
        finally
        {
            coordinator.Exit(contextType);
        }
    }

    private async ValueTask<TResponse> RunOutermost(
        DbContext context,
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        coordinator.Transaction = transaction.GetDbTransaction();

        try
        {
            var response = await next(message, cancellationToken);

            if (response.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            return response;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            coordinator.Transaction = null;
        }
    }

    private async ValueTask<TResponse> RunJoined(
        DbContext context,
        System.Data.Common.DbTransaction outer,
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        // The DbContext that started the transaction already is in it (a command sent from a domain event handler).
        var ownTransaction = context.Database.CurrentTransaction is { } current && current.GetDbTransaction() == outer;
        var transaction = ownTransaction
            ? context.Database.CurrentTransaction!
            : await context.Database.UseTransactionAsync(outer, cancellationToken)
                ?? throw new InvalidOperationException("The database provider does not support joining a transaction.");
        var savepoint = coordinator.NextSavepointName();
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);

        try
        {
            var response = await next(message, cancellationToken);

            if (response.IsSuccess)
            {
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            }
            else
            {
                await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            }

            return response;
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally
        {
            if (!ownTransaction)
            {
                await context.Database.UseTransactionAsync(null, CancellationToken.None);
            }
        }
    }
}
