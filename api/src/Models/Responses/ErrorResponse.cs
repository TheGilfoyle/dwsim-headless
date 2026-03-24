namespace DwsimService.Models.Responses;

public record ErrorResponse(
    string Error,
    string? Detail = null,
    string? Code = null,
    int? Status = null,
    IReadOnlyList<string>? Suggestions = null
);
