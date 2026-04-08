using Amazon.CDK;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.APIGateway;
using Constructs;
using System.Collections.Generic;

namespace DocumentsManager.Infra
{
    public class DocumentsManagerStack : Stack
    {
        internal DocumentsManagerStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
        {
            // S3 Buckets
            var bucketOrigen = new Bucket(this, "BucketOrigen", new BucketProps
            {
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteObjects = true
            });

            var bucketDestino = new Bucket(this, "BucketDestino", new BucketProps
            {
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteObjects = true
            });

            // SNS Topic
            var topic = new Topic(this, "TransactionTopic", new TopicProps
            {
                DisplayName = "Document Transactions"
            });

            // SQS Queue
            var queue = new Queue(this, "TransactionQueue", new QueueProps
            {
                VisibilityTimeout = Duration.Seconds(30),
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            topic.AddSubscription(new SqsSubscription(queue));

            // Roles IAM
            var publisherRole = new Role(this, "PublisherRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            publisherRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "s3:PutObject", "sns:Publish" },
                Resources = new[] { bucketOrigen.BucketArn, topic.TopicArn }
            }));

            var subscriberRole = new Role(this, "SubscriberRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            subscriberRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { 
                    "s3:GetObject", 
                    "sqs:ReceiveMessage", 
                    "sqs:DeleteMessage",
                    "sqs:GetQueueAttributes",
                    "sqs:GetQueueUrl"
                },
                Resources = new[] { bucketOrigen.BucketArn + "/*", queue.QueueArn }
            }));

            var targetRole = new Role(this, "TargetRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            targetRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "s3:PutObject" },
                Resources = new[] { bucketDestino.BucketArn + "/*" }
            }));

            // Asegurar que todos los roles tengan estos permisos básicos
            var logPolicy = new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { 
                    "logs:CreateLogGroup",
                    "logs:CreateLogStream",
                    "logs:PutLogEvents"
                },
                Resources = new[] { "*" }
            });

            publisherRole.AddToPolicy(logPolicy);
            subscriberRole.AddToPolicy(logPolicy);
            targetRole.AddToPolicy(logPolicy);

            // Lambda Publisher
            var publisher = new Function(this, "PublisherFunction", new FunctionProps
            {
                Runtime = Runtime.DOTNET_6,
                Code = Code.FromAsset("../DocumentsManager.Publisher/bin/Release/net8.0/publish"),
                Handler = "DocumentsManager.Publisher::DocumentsManager.Publisher.Function::FunctionHandler",
                Role = publisherRole,
                Timeout = Duration.Seconds(30),
                MemorySize = 256,
                Environment = new Dictionary<string, string>
                {
                    ["BUCKET_NAME"] = bucketOrigen.BucketName,
                    ["SNS_TOPIC_ARN"] = topic.TopicArn
                }
            });

            // API Gateway
            var api = new RestApi(this, "DocumentsApi", new RestApiProps
            {
                RestApiName = "Documents Manager API"
            });

            var documents = api.Root.AddResource("documents");
            var transactions = documents.AddResource("transactions");
            transactions.AddMethod("POST", new LambdaIntegration(publisher));

            // Lambda Subscriber
            var subscriber = new Function(this, "SubscriberFunction", new FunctionProps
            {
                Runtime = Runtime.DOTNET_6,
                Code = Code.FromAsset("../DocumentsManager.Subscriber/bin/Release/net8.0/publish"),
                Handler = "DocumentsManager.Subscriber::DocumentsManager.Subscriber.Function::FunctionHandler",
                Role = subscriberRole,
                Timeout = Duration.Minutes(5),
                MemorySize = 512,
                Environment = new Dictionary<string, string>
                {
                    ["TARGET_API_URL"] = ""
                }
            });

            // Lambda Target
            var target = new Function(this, "TargetFunction", new FunctionProps
            {
                Runtime = Runtime.DOTNET_6,
                Code = Code.FromAsset("../DocumentsManager.Target/bin/Release/net8.0/publish"),
                Handler = "DocumentsManager.Target::DocumentsManager.Target.Function::FunctionHandler",
                Role = targetRole,
                Timeout = Duration.Minutes(2),
                MemorySize = 256,
                Environment = new Dictionary<string, string>
                {
                    ["DEST_BUCKET_NAME"] = bucketDestino.BucketName
                }
            });

            // Target URL
            var targetUrl = target.AddFunctionUrl(new FunctionUrlOptions
            {
                AuthType = FunctionUrlAuthType.NONE
            });

            // SQS Trigger for Subscriber
            new CfnEventSourceMapping(this, "SQSTrigger", new CfnEventSourceMappingProps
            {
                EventSourceArn = queue.QueueArn,
                FunctionName = subscriber.FunctionName,
                Enabled = true,
                BatchSize = 1
            });

            // Update Subscriber environment variable
            var cfnSubscriber = subscriber.Node.DefaultChild as CfnFunction;
            cfnSubscriber?.AddPropertyOverride("Environment.Variables.TARGET_API_URL", targetUrl.Url);

            // Outputs
            new CfnOutput(this, "ApiUrl", new CfnOutputProps { Value = api.Url });
            new CfnOutput(this, "TargetApiUrl", new CfnOutputProps { Value = targetUrl.Url });
            new CfnOutput(this, "BucketOrigenName", new CfnOutputProps { Value = bucketOrigen.BucketName });
            new CfnOutput(this, "BucketDestinoName", new CfnOutputProps { Value = bucketDestino.BucketName });
        }
    }
}