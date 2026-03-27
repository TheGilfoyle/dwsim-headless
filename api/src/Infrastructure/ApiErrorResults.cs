using DwsimService.Models.Responses;

namespace DwsimService.Infrastructure;

public static class ApiErrorResults
{
    public static IResult BadRequest(
        ArgumentException ex,
        string defaultCode = "invalid_request")
    {
        ErrorResponse response = ex is ApiValidationException validationEx
            ? new ErrorResponse(
                Error: validationEx.Message,
                Detail: validationEx.Message,
                Code: validationEx.Code,
                Status: StatusCodes.Status400BadRequest,
                Suggestions: validationEx.Suggestions.Count > 0
                    ? validationEx.Suggestions
                    : null)
            : new ErrorResponse(
                Error: ex.Message,
                Detail: ex.Message,
                Code: defaultCode,
                Status: StatusCodes.Status400BadRequest
            );

        return Results.BadRequest(response);
    }

    public static IResult UnprocessableEntity(
        string error,
        Exception ex,
        string code)
    {
        return Results.Json(
            new ErrorResponse(
                Error: error,
                Detail: ex.Message,
                Code: code,
                Status: StatusCodes.Status422UnprocessableEntity
            ),
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }
}
