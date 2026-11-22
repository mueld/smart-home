namespace Backend.Kernel;

public union Outcome<T, TError>(T, TError, ValidationFailed);

public sealed record ValidationFailed(string Message);
