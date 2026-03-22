using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.Observability;
using Nop.Services.Logging;

namespace Nop.Services.Events;

/// <summary>
/// Represents the event publisher implementation
/// </summary>
public partial class EventPublisher : IEventPublisher
{
    private static readonly HashSet<string> TracedEventNames =
    [
        nameof(OrderPlacedEvent)
    ];

    #region Methods

    /// <summary>
    /// Publish event to consumers
    /// </summary>
    /// <typeparam name="TEvent">Type of event</typeparam>
    /// <param name="event">Event object</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task PublishAsync<TEvent>(TEvent @event)
    {
        //get all event consumers
        var consumers = EngineContext.Current.ResolveAll<IConsumer<TEvent>>().ToList();
        var eventName = typeof(TEvent).Name;
        var publishSucceeded = true;
        var traceEvent = ShouldTraceEvent(eventName);

        using var publishActivity = traceEvent
            ? NopTelemetry.StartEventPublishActivity(eventName, consumers.Count)
            : null;

        foreach (var consumer in consumers)
        {
            try
            {
                //try to handle published event
                await consumer.HandleEventAsync(@event);

                if (@event is IStopProcessingEvent { StopProcessing: true })
                {
                    publishActivity?.SetTag("event.stop_processing", true);
                    break;
                }
            }
            catch (Exception exception)
            {
                publishSucceeded = false;
                publishActivity?.SetTag("event.failed_consumer", consumer.GetType().Name);

                //log error, we put in to nested try-catch to prevent possible cyclic (if some error occurs)
                try
                {
                    var logger = EngineContext.Current.Resolve<ILogger>();
                    if (logger == null)
                        return;

                    await logger.ErrorAsync(exception.Message, exception);
                }
                catch
                {
                    // ignored
                }
            }
        }

        if (publishSucceeded)
            NopTelemetry.MarkSuccess(publishActivity);
        else
            NopTelemetry.MarkFailure(publishActivity, FailureCategories.Unexpected, errorType: "consumer_error");
    }

    private static bool ShouldTraceEvent(string eventName)
    {
        return TracedEventNames.Contains(eventName);
    }

    #endregion
}
