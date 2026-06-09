using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MvcApp.Services;

public class GeminiService : IGeminiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IHttpClientFactory httpFactory, ILogger<GeminiService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<string> AskAsync(string userQuestion, GeminiContext ctx)
    {
        var apiKey = Environment.GetEnvironmentVariable("Gemini_API_Key");
        if (string.IsNullOrEmpty(apiKey))
            return "AI غير مُفعّل. تأكد من إعداد Gemini_API_Key في الـ Secrets.";

        var periodLabel = (ctx.Month.HasValue && ctx.Year.HasValue)
            ? $"{ctx.Month}/{ctx.Year}"
            : "غير محدد";

        var storeLabel = string.IsNullOrEmpty(ctx.Store) ? "جميع الفروع" : ctx.Store;

        var systemPrompt = $"""
            أنت مساعد HR ذكي متخصص في تحليل بيانات الموارد البشرية لشركة Manfoods McDonald's.
            مهمتك هي الإجابة على أسئلة المدراء بناءً على البيانات المتاحة فقط.
            
            === البيانات الحالية ===
            الفترة: {periodLabel}
            الفرع: {storeLabel}
            إجمالي الموظفين (Headcount): {ctx.TotalHeadcount}
            الموظفون الجدد (New Hires): {ctx.NewHires}
            إجمالي الاستقالات: {ctx.TotalResignations}
            معدل الـ Turnover: {ctx.TurnoverRate}%
            ========================
            
            قواعد مهمة:
            - أجب دائمًا بشكل موجز ومفيد.
            - لا تخترع أرقامًا خارج البيانات المعطاة.
            - إذا سُئلت عن بيانات غير متاحة، اذكر ذلك بوضوح.
            - يمكنك الإجابة بالعربية أو الإنجليزية حسب لغة السؤال.
            - قدم توصيات عملية عند الطلب.
            
            سؤال المستخدم: {userQuestion}
            """;

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 1024
            }
        };

        try
        {
            var client = _httpFactory.CreateClient();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini API error {Status}: {Body}", response.StatusCode, responseJson);
                return "تعذر الحصول على إجابة من الـ AI. حاول مرة أخرى.";
            }

            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text ?? "لم يتم الحصول على إجابة.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini service exception");
            return "حدث خطأ أثناء الاتصال بالـ AI. تأكد من الاتصال بالإنترنت وحاول مرة أخرى.";
        }
    }
}
