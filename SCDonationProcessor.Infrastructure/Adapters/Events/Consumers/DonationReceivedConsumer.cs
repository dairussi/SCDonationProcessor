using Application.Common.Ports;
using Application.Events;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Adapters.Events.Consumers;

public class DonationReceivedConsumer
{
    private readonly IDonationProcessingService _donationProcessingService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DonationReceivedConsumer> _logger;

    public DonationReceivedConsumer(
        IDonationProcessingService donationProcessingService,
        IEventPublisher eventPublisher,
        ILogger<DonationReceivedConsumer> logger)
    {
        _donationProcessingService = donationProcessingService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task ConsumeAsync(
        DonationReceivedEvent donationEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Recebido DonationReceivedEvent - DonationId: {DonationId}, CampaignId: {CampaignId}, DonorId: {DonorId}, Amount: {Amount}",
            donationEvent.DonationId,
            donationEvent.CampaignId,
            donationEvent.DonorId,
            donationEvent.Amount);

        var donationProcessedEvent = await _donationProcessingService.ProcessDonationAsync(donationEvent);

        await _eventPublisher.PublishAsync(donationProcessedEvent, cancellationToken);

        _logger.LogInformation(
            "DonationProcessedEvent publicado - DonationId: {DonationId}, Status: {Status}, CampaignId: {CampaignId}, DonorId: {DonorId}",
            donationProcessedEvent.DonationId,
            donationProcessedEvent.Status,
            donationProcessedEvent.CampaignId,
            donationProcessedEvent.DonorId);
    }
}
