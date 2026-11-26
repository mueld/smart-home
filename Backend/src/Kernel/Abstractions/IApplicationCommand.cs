using Mediator;

namespace Backend.Kernel.Abstractions;

/// <summary>
/// Represents an application command without a return value.
/// </summary>
public interface IApplicationCommand : ICommand;

/// <summary>
/// Represents an application command with a typed result.
/// </summary>
public interface IApplicationCommand<TResponse, TError>
    : ICommand<Outcome<TResponse, TError>>
    where TError : struct;
