using System.Text.Json;

namespace NotificationService.Models
{
    public class SQSMessage
    {
        public required string MessageType { get; set; }
        public required string Payload { get; set; }
        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }
        public static SQSMessage FromJson(string json)
        {
            return JsonSerializer.Deserialize<SQSMessage>(json) ?? throw new ArgumentException("Invalid JSON");
        }
    }
}