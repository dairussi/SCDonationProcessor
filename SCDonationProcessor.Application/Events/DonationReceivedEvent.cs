namespace Application.Events;

public class DonationReceivedEvent
{
    public Guid DonationId { get; init; }
    public Guid CampaignId { get; init; }
    public int DonorId { get; init; }
    public decimal Amount { get; init; }
    public DateTime CreatedAt { get; init; }
}
