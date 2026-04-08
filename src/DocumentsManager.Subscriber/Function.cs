using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace DocumentsManager.Subscriber;

public class Function
{
    private readonly HttpClient _httpClient;

    public Function()
    {
        _httpClient = new HttpClient();
    }

    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var targetApiUrl = Environment.GetEnvironmentVariable("TARGET_API_URL");
        
        foreach (var record in evnt.Records)
        {
            try
            {
                context.Logger.LogInformation($"Processing message: {record.MessageId}");
                
                var message = JsonSerializer.Deserialize<TransactionMessage>(record.Body);
                
                context.Logger.LogInformation($"Transaction: {message?.TransactionId}");
                context.Logger.LogInformation($"File URL: {message?.FileUrl}");
                
                // Simular trabajo asíncrono
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error: {ex.Message}");
                throw;
            }
        }
    }
}

public class TransactionMessage
{
    public string TransactionId { get; set; } = "";
    public string FileUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public string S3Key { get; set; } = "";
    public DateTime Timestamp { get; set; }
}