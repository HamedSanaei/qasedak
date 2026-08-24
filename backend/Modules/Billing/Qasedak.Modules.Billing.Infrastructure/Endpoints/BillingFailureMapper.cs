using Microsoft.AspNetCore.Http;
using Qasedak.Modules.Billing.Domain.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Endpoints;

/// <summary>
/// Maps billing rule codes to HTTP results. Foreign resources stay 404 (tenant
/// isolation); wrong-state races surface as 409; unknown/disabled providers as 400.
/// </summary>
public static class BillingFailureMapper
{
    public static IResult ToResult(string code) => code switch
    {
        PaymentFailures.NotFound or PaymentFailures.PlanNotFound
            => Results.Json(new { code }, statusCode: StatusCodes.Status404NotFound),
        PaymentFailures.WrongState
            => Results.Json(new { code }, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { code }, statusCode: StatusCodes.Status400BadRequest),
    };
}
