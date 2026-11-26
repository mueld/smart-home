using Backend.Kernel;
using Mediator;

namespace GMB.Shared.Application.Messaging;

/// <summary>
/// Defines a query handler that enforces the use of the <c>Result</c> pattern for consistent application-layer responses.
/// </summary>
/// <typeparam name="TQuery">The type of the query request.</typeparam>
/// <typeparam name="TResponse">The type of the response returned on success.</typeparam>
/// <remarks>
/// This interface ensures that all application query handlers return a <see cref="Result"/>,
/// enabling uniform handling of success and failure across the application.
/// </remarks>
public interface IApplicationQueryHandler<in TQuery, TResponse> : IQueryHandler<TQuery, Outcome<TResponse, NotFound>>
    where TQuery : IApplicationQuery<TResponse>;
