using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace DocumentsManager.Publisher;

public class Function
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonSimpleNotificationService _snsClient;

    public Function()
    {
        _s3Client = new AmazonS3Client();
        _snsClient = new AmazonSimpleNotificationServiceClient();
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            context.Logger.LogInformation("Processing document transaction request");
            
            var transactionId = Guid.NewGuid().ToString();
            var fileName = $"document_{transactionId}.txt";
            var timestamp = DateTime.UtcNow;
            
            var documentContent = $"Transaction ID: {transactionId}\nCreated: {timestamp}\nStatus: Processing";
            var fileContent = Encoding.UTF8.GetBytes(documentContent);
            var s3Key = $"uploads/{timestamp:yyyy/MM/dd}/{fileName}";
            
            var bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME");
            var snsTopicArn = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN");
            
            await _s3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = bucketName,
                Key = s3Key,
                InputStream = new MemoryStream(fileContent)
            });
            
            var fileUrl = _s3Client.GetPreSignedURL(new Amazon.S3.Model.GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = s3Key,
                Expires = DateTime.UtcNow.AddHours(1)
            });
            
            var message = new
            {
                TransactionId = transactionId,
                FileUrl = fileUrl,
                FileName = fileName,
                S3Key = s3Key,
                Timestamp = timestamp
            };
            
            await _snsClient.PublishAsync(new PublishRequest
            {
                TopicArn = snsTopicArn,
                Message = JsonSerializer.Serialize(message)
            });

            context.Logger.LogInformation($"Message published to SNS: {snsTopicArn}");
            
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonSerializer.Serialize(new { success = true, transactionId })
            };
        }
        catch (Exception ex)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Body = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}