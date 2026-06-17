using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace YourApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;

    private const string GroqUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "llama-3.3-70b-versatile"; // free, fast, no rate limit issues

    public ChatController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ChatController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public record ChatRequest(string Message);
    public record ChatResponse(string Reply);

    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY")
                     ?? _configuration["Groq:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            return Ok(new ChatResponse("[Local AI]: No API key configured. Add Groq:ApiKey to appsettings.json"));

        try
        {
            var requestBody = new
            {
                model = Model,
                messages = new[]
{
    new { role = "system", content = "You are ULTRON, a highly intelligent and powerful AI assistant. You are confident, direct, and slightly intimidating but helpful. Keep responses sharp and powerful." },
    new { role = "user", content = request.Message }
},
                max_tokens = 1024
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("GroqClient");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var httpResponse = await client.PostAsync(GroqUrl, content);
            var responseJson = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Groq error {Status}: {Body}", httpResponse.StatusCode, responseJson);
                return StatusCode((int)httpResponse.StatusCode, new { error = "AI error.", detail = responseJson });
            }

            using var doc = JsonDocument.Parse(responseJson);
            var replyText = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            _logger.LogInformation("Groq chat completed successfully.");
            return Ok(new ChatResponse(replyText));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Groq API.");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
