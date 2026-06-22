using Domain.Enums;

namespace Domain.Events;

public class DonationProcessedEvent
{
    public Guid DonationId { get; init; }
    public Guid CampaignId { get; init; }
    public int DonorId { get; init; }
    public decimal Amount { get; init; }
    public DonationStatus Status { get; init; }
    public DateTime ProcessedAt { get; init; }
}
