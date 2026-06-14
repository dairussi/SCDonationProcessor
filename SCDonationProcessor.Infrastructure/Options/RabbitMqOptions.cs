namespace Infrastructure.Options;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string DonationReceivedQueue { get; set; } = "donation-received";
    public string DonationProcessedQueue { get; set; } = "donation-processed";
}
