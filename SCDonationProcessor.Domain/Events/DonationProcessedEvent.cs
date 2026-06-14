namespace Domain.Events;

public class DonationProcessedEvent
{
    public Guid DonationId { get; init; }
    public Guid CampaignId { get; init; }
    public int DonorId { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}
