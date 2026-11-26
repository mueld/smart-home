using Backend.Kernel;
using Mediator;

namespace GMB.Shared.Application.Messaging;

/// <summary>
/// Represents a query that returns a <see cref="Result"/>, enforcing a consistent result-handling pattern in the application layer.
/// </summary>
/// <typeparam name="TResponse">The type of the value returned on successful execution of the query.</typeparam>
/// <remarks>
/// This interface ensures that queries produce results wrapped in a <c>Result</c> object, enabling standardized handling
/// of success and failure cases across the application.
/// </remarks>
public interface IApplicationQuery<TResponse> : IQuery<Outcome<TResponse, NotFound>>;
