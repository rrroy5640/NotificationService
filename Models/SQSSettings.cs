namespace NotificationService.Models{
    public class SQSSettings: ISQSSettings{
        public required string QueueUrl { get; set; }
    }

    public interface ISQSSettings
    {
        string QueueUrl { get; set; }
    }
}