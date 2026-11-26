using Backend.Kernel;
using Backend.Kernel.Abstractions;
using Mediator;

namespace GMB.Shared.Application.Messaging;

/// <summary>
/// Handles an application command without a return value.
/// </summary>
public interface IApplicationCommandHandler<in TCommand> : ICommandHandler<TCommand>
    where TCommand : IApplicationCommand;

/// <summary>
/// Handles an application command with a typed result.
/// </summary>
public interface IApplicationCommandHandler<in TCommand, TResponse, TError> : ICommandHandler<TCommand, Outcome<TResponse, TError>>
    where TCommand : IApplicationCommand<TResponse, TError>
    where TError : struct;