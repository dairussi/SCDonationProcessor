using Application.Common.Ports;
using Application.Events;
using Domain.Events;

namespace Application.Services;

public class DonationProcessingService : IDonationProcessingService
{
    public Task<DonationProcessedEvent> ProcessDonationAsync(DonationReceivedEvent donationEvent)
    {
        var isApproved = Random.Shared.Next(1, 101) <= 90;

        var donationProcessedEvent = new DonationProcessedEvent
        {
            DonationId = donationEvent.DonationId,
            CampaignId = donationEvent.CampaignId,
            DonorId = donationEvent.DonorId,
            Amount = donationEvent.Amount,
            Status = isApproved ? "Approved" : "Rejected",
            ProcessedAt = DateTime.UtcNow
        };

        return Task.FromResult(donationProcessedEvent);
    }
}
