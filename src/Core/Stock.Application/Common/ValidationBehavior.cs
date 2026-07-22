using FluentValidation;
using MediatR;
using Stock.Domain.Errors;

namespace Stock.Application.Common;

/// <summary>MediatR pipeline step: runs FluentValidation validators before
/// any handler and surfaces failures as typed domain errors.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, ct);
            if (!result.IsValid)
            {
                var message = string.Join(" ", result.Errors.Select(e => e.ErrorMessage));
                throw DomainException.Validation(message);
            }
        }

        return await next();
    }
}
