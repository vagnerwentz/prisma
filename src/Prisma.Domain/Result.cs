using System.Diagnostics.CodeAnalysis;

namespace Prisma.Domain;

public enum ErrorType { Validation, Unauthorized, Forbidden, NotFound, Conflict, TooManyAttempts }

// Message é exibida ao usuário, portanto em pt-BR.
public sealed record Error(ErrorType Type, string Message);

// Erro de negócio esperado. Exceção fica para o que é de fato excepcional.
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result com erro não tem valor.");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
