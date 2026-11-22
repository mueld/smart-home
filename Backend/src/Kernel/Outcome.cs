namespace Backend.Kernel;

/// <summary>
/// Result of a use case: success <typeparamref name="T"/>, an expected error <typeparamref name="TError"/>
/// (the error union of the use case, AD-10) or <see cref="ValidationFailed"/> from the pipeline.
/// </summary>
public union Outcome<T, TError>(T, TError, ValidationFailed) : IOutcome<Outcome<T, TError>>
{
    public static Outcome<T, TError> FromValidationFailed(ValidationFailed failure) => failure;

    public bool IsSuccess => Value is T;
}

public sealed record ValidationFailed(string Message, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Non-generic view for code that only needs to know whether a response is a failure.</summary>
public interface IOutcomeState
{
    bool IsSuccess { get; }
}

/// <summary>Lets pipeline behaviors create an outcome without knowing its cases.</summary>
public interface IOutcome<TSelf> : IOutcomeState where TSelf : IOutcome<TSelf>
{
    static abstract TSelf FromValidationFailed(ValidationFailed failure);
}
