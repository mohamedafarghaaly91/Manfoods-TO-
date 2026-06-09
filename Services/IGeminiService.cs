namespace MvcApp.Services;

public class GeminiContext
{
    public int? Month { get; set; }
    public int? Year { get; set; }
    public string? Store { get; set; }
    public int TotalHeadcount { get; set; }
    public int TotalResignations { get; set; }
    public double TurnoverRate { get; set; }
    public int NewHires { get; set; }
}

public interface IGeminiService
{
    Task<string> AskAsync(string userQuestion, GeminiContext context);
}
