using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NotificationService.Models;

namespace NotificationService.Services
{
    public class MessageProcessingService : BackgroundService
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly string _queueUrl;
        private readonly IMongoCollection<SQSMessage> _messages;
        private readonly ILogger<MessageProcessingService> _logger;

        public MessageProcessingService(
            IAmazonSQS sqsClient, 
            IOptions<SQSSettings> sqsSettings,
            IMongoClient mongoClient, 
            IOptions<MongoDBSettings> mongoSettings,
            ILogger<MessageProcessingService> logger)
        {
            _sqsClient = sqsClient;
            _queueUrl = sqsSettings.Value.QueueUrl;
            var database = mongoClient.GetDatabase(mongoSettings.Value.DatabaseName);
            _messages = database.GetCollection<SQSMessage>("messages");
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var message = await ReceiveMessageAsync(stoppingToken);
                    if (message != null)
                    {
                        await ProcessMessageAsync(message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        private async Task<SQSMessage> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 20
            };

            var response = await _sqsClient.ReceiveMessageAsync(request, cancellationToken);

            if (response.Messages.Count > 0)
            {
                var message = response.Messages[0];
                var sqsMessage = SQSMessage.FromJson(message.Body);
                await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, cancellationToken);
                return sqsMessage;
            }

            return null;
        }

        private async Task ProcessMessageAsync(SQSMessage message)
        {
            _logger.LogInformation($"Processing message of type: {message.MessageType}");

            switch (message.MessageType)
            {
                case "SendEmail":
                    await InsertMessageAsync(message);
                    // 这里将来可以添加发送邮件的逻辑
                    break;
                // 添加其他消息类型的处理逻辑
                default:
                    _logger.LogWarning($"Unknown message type: {message.MessageType}");
                    break;
            }
        }

        private async Task InsertMessageAsync(SQSMessage message)
        {
            await _messages.InsertOneAsync(message);
            _logger.LogInformation($"Message inserted into database: {message.MessageType}");
        }
    }
}