namespace DwsimService.Infrastructure;

public class ApiValidationException : ArgumentException
{
    public ApiValidationException(
        string message,
        string code,
        params string[] suggestions)
        : base(message)
    {
        Code = code;
        Suggestions = suggestions;
    }

    public string Code { get; }

    public IReadOnlyList<string> Suggestions { get; }
}
