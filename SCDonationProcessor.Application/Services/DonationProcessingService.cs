using Application.Common.Ports;
using Application.Events;
using Domain.Enums;
using Domain.Events;

namespace Application.Services;

public class DonationProcessingService : IDonationProcessingService
{
    public Task<DonationProcessedEvent> ProcessDonationAsync(DonationReceivedEvent donationEvent)
    {
        var randomNumber = Random.Shared.Next(1, 101);

        var status = randomNumber switch
        {
            <= 70 => DonationStatus.Paid,
            <= 90 => DonationStatus.Pending,
            _ => DonationStatus.Rejected
        };

        var donationProcessedEvent = new DonationProcessedEvent
        {
            DonationId = donationEvent.DonationId,
            CampaignId = donationEvent.CampaignId,
            DonorId = donationEvent.DonorId,
            Amount = donationEvent.Amount,
            Status = status,
            ProcessedAt = DateTime.UtcNow
        };

        return Task.FromResult(donationProcessedEvent);
    }
}
