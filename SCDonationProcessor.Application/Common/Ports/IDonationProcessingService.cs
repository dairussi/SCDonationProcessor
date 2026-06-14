using Application.Events;
using Domain.Events;

namespace Application.Common.Ports;

public interface IDonationProcessingService
{
    Task<DonationProcessedEvent> ProcessDonationAsync(DonationReceivedEvent donationEvent);
}
