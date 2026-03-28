#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Nop.Core.Infrastructure.Observability;

/// <summary>
/// Central telemetry primitives and safe-tag helpers for nopCommerce observability.
/// </summary>
public static class NopTelemetry
{
    public const string ServiceName = "nopcommerce-web";
    public const string ActivitySourceName = "NopCommerce.Observability";
    public const string MeterName = "NopCommerce.Observability";

    private const string Unknown = "unknown";
    private const string BusinessFailureRecordedExceptionDataKey = "NopTelemetry.BusinessFailureRecorded";
    private static readonly HashSet<string> SuppressedCheckoutRepositoryEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "GenericAttribute",
        "OrderNote",
        "QueuedEmail"
    };

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> CheckoutStageDurationSeconds = Meter.CreateHistogram<double>(
        "checkout_stage_duration_seconds",
        unit: "s",
        description: "Duration of checkout stages for the order placement flow.");

    public static readonly Counter<long> CheckoutBusinessFailuresTotal = Meter.CreateCounter<long>(
        "checkout_business_failures_total",
        description: "Business failures encountered during checkout.");

    public static readonly Counter<long> CheckoutOutcomesTotal = Meter.CreateCounter<long>(
        "checkout_outcomes_total",
        description: "Overall checkout outcomes for the order placement flow.");

    public static Activity? StartCheckoutStage(string stage, CheckoutTelemetryContext context)
    {
        var activity = ActivitySource.StartActivity(stage, ActivityKind.Internal);
        ApplyCheckoutTags(activity, context);
        activity?.SetTag(TelemetryTagNames.Stage, stage);
        EnsureSpanDescription(activity);
        return activity;
    }

    public static Activity? StartRepositoryActivity(string operation, string entityName, string? dbSystem, int? batchSize = null)
    {
        var parentActivity = Activity.Current;
        if (ShouldSuppressRepositoryActivity(parentActivity, entityName))
            return null;

        var activity = ActivitySource.StartActivity($"db.repository.{operation}", ActivityKind.Client);
        ApplyCheckoutTags(activity, parentActivity);
        activity?.SetTag(TelemetryTagNames.DbSystem, NormalizeDbSystem(dbSystem));
        activity?.SetTag(TelemetryTagNames.DbOperation, operation);
        activity?.SetTag(TelemetryTagNames.DbEntityName, entityName);

        if (batchSize.HasValue)
            activity?.SetTag(TelemetryTagNames.DbBatchSize, batchSize.Value);

        EnsureSpanDescription(activity);
        return activity;
    }

    private static bool ShouldSuppressRepositoryActivity(Activity? parentActivity, string entityName)
    {
        return TryGetCheckoutContext(parentActivity, out _)
               && SuppressedCheckoutRepositoryEntities.Contains(entityName);
    }

    public static Activity? StartEventPublishActivity(string eventName, int consumerCount)
    {
        var parentActivity = Activity.Current;
        var activity = ActivitySource.StartActivity("nop.event.publish", ActivityKind.Internal);
        ApplyCheckoutTags(activity, parentActivity);
        activity?.SetTag(TelemetryTagNames.EventName, eventName);
        activity?.SetTag(TelemetryTagNames.ConsumerCount, consumerCount);
        EnsureSpanDescription(activity);
        return activity;
    }

    public static Activity? StartEventConsumerActivity(string eventName, string consumerName)
    {
        var parentActivity = Activity.Current;
        var activity = ActivitySource.StartActivity("nop.event.consumer", ActivityKind.Internal);
        ApplyCheckoutTags(activity, parentActivity);
        activity?.SetTag(TelemetryTagNames.EventName, eventName);
        activity?.SetTag(TelemetryTagNames.ConsumerName, consumerName);
        EnsureSpanDescription(activity);
        return activity;
    }

    public static void ApplyCheckoutTags(Activity? activity, Activity? sourceActivity)
    {
        if (activity is null || sourceActivity is null)
            return;

        if (TryGetCheckoutContext(sourceActivity, out var context))
            ApplyCheckoutTags(activity, context);
    }

    public static void ApplyCheckoutTags(Activity? activity, CheckoutTelemetryContext context)
    {
        if (activity is null)
            return;

        activity.SetTag(TelemetryTagNames.CheckoutVariant, context.Variant);
        activity.SetTag(TelemetryTagNames.StoreId, context.StoreId);
        activity.SetTag(TelemetryTagNames.CartItemsCount, context.CartItemsCount);
        activity.SetTag(TelemetryTagNames.PaymentMethodSystemName, context.PaymentMethodSystemName);
        activity.SetTag(TelemetryTagNames.IsRecurring, context.IsRecurring);
        activity.SetTag(TelemetryTagNames.OrderTotalRange, context.OrderTotalRange);
    }

    public static void MarkSuccess(Activity? activity)
    {
        activity?.SetTag(TelemetryTagNames.Result, CheckoutResults.Success);
    }

    public static void MarkFailure(Activity? activity, string failureCategory, string? failureStage = null, string? errorType = null)
    {
        if (activity is null)
            return;

        activity.SetTag(TelemetryTagNames.Result, CheckoutResults.Failure);

        if (!string.IsNullOrWhiteSpace(failureStage))
            activity.SetTag(TelemetryTagNames.FailureStage, failureStage);

        activity.SetTag(TelemetryTagNames.FailureCategory, NormalizeValue(failureCategory));

        if (!string.IsNullOrWhiteSpace(errorType))
            activity.SetTag(TelemetryTagNames.ErrorType, errorType);

        activity.SetStatus(ActivityStatusCode.Error, NormalizeValue(failureCategory));
    }

    public static void FinalizeCheckoutStage(
        Activity? activity,
        string stage,
        CheckoutTelemetryContext context,
        TimeSpan duration,
        CheckoutStageResult result,
        string? errorType = null,
        bool recordBusinessFailure = true)
    {
        if (result.Success)
            MarkSuccess(activity);
        else
            MarkFailure(activity, result.FailureCategory, stage, errorType);

        RecordCheckoutStageDuration(stage, context, duration, result.Result);

        if (!result.Success && recordBusinessFailure)
            RecordCheckoutBusinessFailure(stage, result.FailureCategory, context);
    }

    public static async Task<T> RunCheckoutStageAsync<T>(
        string stage,
        CheckoutTelemetryContext context,
        Func<Task<T>> operation,
        Func<T, CheckoutStageResult>? evaluateResult = null)
    {
        using var activity = StartCheckoutStage(stage, context);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await ApplyInjectedDelayAsync(stage);
            MaybeThrowInjectedFailure(stage);

            var value = await operation();
            var result = evaluateResult?.Invoke(value) ?? CheckoutStageResult.SuccessResult();

            FinalizeCheckoutStage(activity, stage, context, stopwatch.Elapsed, result);

            return value;
        }
        catch (Exception exception)
        {
            var failureCategory = ClassifyException(stage, exception);
            FinalizeCheckoutStage(
                activity,
                stage,
                context,
                stopwatch.Elapsed,
                CheckoutStageResult.FailureResult(failureCategory),
                exception.GetType().Name,
                recordBusinessFailure: false);

            if (TryMarkBusinessFailureRecorded(exception))
                RecordCheckoutBusinessFailure(stage, failureCategory, context);

            throw;
        }
    }

    public static bool HasRecordedBusinessFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Data.Contains(BusinessFailureRecordedExceptionDataKey);
    }

    public static bool TryMarkBusinessFailureRecorded(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.Data.Contains(BusinessFailureRecordedExceptionDataKey))
            return false;

        exception.Data[BusinessFailureRecordedExceptionDataKey] = true;
        return true;
    }

    public static async Task RunCheckoutStageAsync(string stage, CheckoutTelemetryContext context, Func<Task> operation)
    {
        await RunCheckoutStageAsync<object?>(
            stage,
            context,
            async () =>
            {
                await operation();
                return null;
            });
    }

    private static async Task ApplyInjectedDelayAsync(string stage)
    {
        var settings = FaultInjectionSettings.Load();
        if (!settings.Enabled
            || settings.DelayMilliseconds <= 0
            || !string.Equals(settings.DelayStage, stage, StringComparison.OrdinalIgnoreCase)
            || !ShouldInject(settings.DelayPercent))
        {
            return;
        }

        TagInjectedFault(Activity.Current, stage, FaultTypes.Delay, settings.DelayPercent, settings.DelayMilliseconds);
        await Task.Delay(settings.DelayMilliseconds);
    }

    private static void MaybeThrowInjectedFailure(string stage)
    {
        var settings = FaultInjectionSettings.Load();
        if (!settings.Enabled
            || !string.Equals(settings.FailureStage, stage, StringComparison.OrdinalIgnoreCase)
            || !ShouldInject(settings.FailurePercent))
        {
            return;
        }

        TagInjectedFault(Activity.Current, stage, FaultTypes.Failure, settings.FailurePercent);
        throw new InvalidOperationException($"Injected checkout failure at stage '{stage}'.");
    }

    private static bool ShouldInject(int percent)
    {
        return percent > 0 && Random.Shared.Next(1, 101) <= percent;
    }

    private static void TagInjectedFault(Activity? activity, string stage, string faultType, int percent, int? delayMilliseconds = null)
    {
        if (activity is null)
            return;

        activity.SetTag(TelemetryTagNames.FaultInjected, true);
        activity.SetTag(TelemetryTagNames.FaultType, faultType);
        activity.SetTag(TelemetryTagNames.FaultStage, stage);
        activity.SetTag(TelemetryTagNames.FaultPercent, percent);

        if (delayMilliseconds.HasValue)
            activity.SetTag(TelemetryTagNames.FaultDelayMilliseconds, delayMilliseconds.Value);
    }

    public static void RecordCheckoutStageDuration(string stage, CheckoutTelemetryContext context, TimeSpan duration, string result)
    {
        var tags = CreateCheckoutMetricTags(context);
        tags.Add(TelemetryTagNames.Stage, stage);
        tags.Add(TelemetryTagNames.Result, NormalizeValue(result));

        CheckoutStageDurationSeconds.Record(duration.TotalSeconds, tags);
    }

    public static void RecordCheckoutBusinessFailure(string stage, string failureCategory, CheckoutTelemetryContext context)
    {
        var tags = CreateCheckoutMetricTags(context);
        tags.Add(TelemetryTagNames.Stage, stage);
        tags.Add(TelemetryTagNames.FailureCategory, NormalizeValue(failureCategory));

        CheckoutBusinessFailuresTotal.Add(1, tags);
    }

    public static void RecordCheckoutOutcome(string result, CheckoutTelemetryContext context)
    {
        var tags = CreateCheckoutMetricTags(context);
        tags.Add(TelemetryTagNames.Result, NormalizeValue(result));

        CheckoutOutcomesTotal.Add(1, tags);
    }

    public static bool TryGetCheckoutContext(Activity? activity, out CheckoutTelemetryContext context)
    {
        return TryCreateCheckoutContext(activity, null, null, null, null, null, null, out context);
    }

    public static bool TryCreateCheckoutContext(
        Activity? activity,
        string? variant,
        int? storeId,
        int? cartItemsCount,
        string? paymentMethodSystemName,
        bool? isRecurring,
        decimal? orderTotal,
        out CheckoutTelemetryContext context)
    {
        var resolvedVariant = NormalizeValue(variant ?? GetStringTag(activity, TelemetryTagNames.CheckoutVariant));
        if (resolvedVariant == Unknown)
        {
            context = default;
            return false;
        }

        context = new CheckoutTelemetryContext(
            Variant: resolvedVariant,
            StoreId: storeId ?? GetIntTag(activity, TelemetryTagNames.StoreId),
            CartItemsCount: cartItemsCount ?? GetIntTag(activity, TelemetryTagNames.CartItemsCount),
            PaymentMethodSystemName: NormalizePaymentMethod(paymentMethodSystemName ?? GetStringTag(activity, TelemetryTagNames.PaymentMethodSystemName)),
            IsRecurring: isRecurring ?? GetBooleanTag(activity, TelemetryTagNames.IsRecurring),
            OrderTotalRange: orderTotal.HasValue
                ? BucketOrderTotal(orderTotal)
                : NormalizeValue(GetStringTag(activity, TelemetryTagNames.OrderTotalRange)));

        return true;
    }

    public static string BucketOrderTotal(decimal? orderTotal)
    {
        if (!orderTotal.HasValue)
            return Unknown;

        return orderTotal.Value switch
        {
            <= 0m => "zero",
            <= 50m => "0-50",
            <= 100m => "50-100",
            <= 250m => "100-250",
            <= 500m => "250-500",
            <= 1000m => "500-1000",
            _ => "1000_plus"
        };
    }

    public static string ClassifyException(string stage, Exception exception)
    {
        if (exception is TimeoutException || ContainsTypeName(exception, "Timeout"))
        {
            return stage is CheckoutStages.PaymentProcess or CheckoutStages.PaymentPostProcess
                ? FailureCategories.PaymentTimeout
                : FailureCategories.Unexpected;
        }

        if (ContainsTypeName(exception, "SqlException")
            || ContainsTypeName(exception, "NpgsqlException")
            || ContainsTypeName(exception, "MySqlException")
            || ContainsTypeName(exception, "DbException"))
            return FailureCategories.Db;

        if (stage == CheckoutStages.InventoryAdjust || ContainsText(exception, "inventory") || ContainsText(exception, "stock"))
            return FailureCategories.Inventory;

        if (stage is CheckoutStages.PaymentProcess or CheckoutStages.PaymentPostProcess
            || ContainsText(exception, "payment")
            || ContainsText(exception, "credit card"))
            return FailureCategories.PaymentDeclined;

        if (stage == CheckoutStages.ConfirmOrder || ContainsText(exception, "captcha") || ContainsText(exception, "checkout"))
            return FailureCategories.Validation;

        return FailureCategories.Unexpected;
    }

    public static string NormalizePaymentMethod(string? paymentMethodSystemName)
    {
        return string.IsNullOrWhiteSpace(paymentMethodSystemName) ? "none" : paymentMethodSystemName.Trim().ToLowerInvariant();
    }

    public static void EnsureSpanDescription(Activity? activity)
    {
        if (activity is null || !string.IsNullOrWhiteSpace(GetStringTag(activity, TelemetryTagNames.Description)))
            return;

        var description = DescribeSpan(activity);
        if (!string.IsNullOrWhiteSpace(description))
            activity.SetTag(TelemetryTagNames.Description, description);
    }

    public static string? DescribeSpan(Activity? activity)
    {
        if (activity is null)
            return null;

        var stage = GetStringTag(activity, TelemetryTagNames.Stage);
        if (!string.IsNullOrWhiteSpace(stage))
            return DescribeCheckoutStage(stage);

        var dbOperation = GetStringTag(activity, TelemetryTagNames.DbOperation);
        var dbEntityName = GetStringTag(activity, TelemetryTagNames.DbEntityName);
        if (!string.IsNullOrWhiteSpace(dbOperation) && !string.IsNullOrWhiteSpace(dbEntityName))
            return DescribeRepositorySpan(dbOperation, dbEntityName);

        var eventName = GetStringTag(activity, TelemetryTagNames.EventName);
        if (!string.IsNullOrWhiteSpace(eventName))
            return DescribeEventSpan(activity.OperationName, eventName);

        return activity.Kind == ActivityKind.Server
            ? DescribeHttpServerSpan(activity)
            : $"Represents the {activity.OperationName} operation in the current request flow.";
    }

    private static string DescribeCheckoutStage(string stage)
    {
        return stage switch
        {
            CheckoutStages.ConfirmOrder => "Validates the checkout confirmation request before the order is placed.",
            CheckoutStages.PlaceOrder => "Coordinates the end-to-end order placement workflow after checkout confirmation.",
            CheckoutStages.PrepareDetails => "Prepares and validates the cart, customer, totals, shipping, and payment details needed to place the order.",
            CheckoutStages.PaymentProcess => "Processes or authorizes the selected payment method for the order being placed.",
            CheckoutStages.PaymentPostProcess => "Runs the post-payment step required after the order is created, such as redirect or final payment handling.",
            CheckoutStages.OrderSave => "Persists the core order record and its address snapshots to the database.",
            CheckoutStages.OrderItemsMove => "Converts shopping cart items into order items and clears the purchased cart entries.",
            CheckoutStages.InventoryAdjust => "Updates product inventory and records the stock movement caused by the purchase.",
            CheckoutStages.EventPublish => "Publishes the business event emitted after the order has been placed successfully.",
            _ => $"Represents the {stage} stage of the checkout pipeline."
        };
    }

    private static string DescribeRepositorySpan(string operation, string entityName)
    {
        return (NormalizeValue(operation), entityName) switch
        {
            ("insert", "Address") => "Persists a billing or shipping address snapshot that will be attached to the order.",
            ("insert", "Order") => "Creates the core order record in the database.",
            ("update", "Order") => "Updates the persisted order after identifiers, totals, or status fields are finalized.",
            ("insert", "OrderItem") => "Creates an order item row for a purchased cart item.",
            ("update", "Product") => "Updates the product record after checkout changed inventory-related values.",
            ("update", "Customer") => "Updates customer state affected by checkout, such as last activity or checkout-related metadata.",
            ("insert", "StockQuantityHistory") => "Records an audit entry for the stock adjustment caused by this purchase.",
            ("bulk_delete", "ShoppingCartItem") => "Removes purchased items from the shopping cart after they are converted into order items.",
            _ => $"{ToSentenceVerb(operation)} the {entityName} entity through the repository layer as part of this request."
        };
    }

    private static string DescribeEventSpan(string operationName, string eventName)
    {
        return operationName switch
        {
            "checkout.event.publish" => $"Publishes the {eventName} business event as part of the checkout completion flow.",
            "nop.event.publish" => $"Publishes the {eventName} domain event to in-process nopCommerce consumers.",
            "nop.event.consumer" => $"Executes an in-process consumer for the {eventName} domain event.",
            _ => $"Represents event-processing work for {eventName}."
        };
    }

    private static string DescribeHttpServerSpan(Activity activity)
    {
        var method = GetStringTag(activity, "http.request.method") ?? "HTTP";
        var path = GetStringTag(activity, "url.path") ?? "/";

        if (path.Contains("/checkout/OpcConfirmOrder", StringComparison.OrdinalIgnoreCase))
            return $"Incoming {method} request that confirms the order in the one-page checkout flow.";

        if (path.Contains("/checkout/confirm", StringComparison.OrdinalIgnoreCase))
            return $"Incoming {method} request that confirms the order in the standard checkout flow.";

        return $"Incoming {method} request for {path}.";
    }

    private static string ToSentenceVerb(string operation)
    {
        return NormalizeValue(operation) switch
        {
            "insert" => "Creates",
            "update" => "Updates",
            "delete" => "Deletes",
            "bulk_delete" => "Deletes",
            "delete_by_predicate" => "Deletes",
            _ => "Performs"
        };
    }

    private static TagList CreateCheckoutMetricTags(CheckoutTelemetryContext context)
    {
        var tags = new TagList
        {
            { TelemetryTagNames.CheckoutVariant, context.Variant },
            { TelemetryTagNames.PaymentMethodSystemName, context.PaymentMethodSystemName },
            { TelemetryTagNames.IsRecurring, context.IsRecurring ? "true" : "false" }
        };

        return tags;
    }

    private static string NormalizeDbSystem(string? dbSystem)
    {
        var normalized = NormalizeValue(dbSystem);

        return normalized switch
        {
            "mssql" or "sqlserver" => "mssql",
            "postgresql" or "postgres" => "postgresql",
            "mysql" => "mysql",
            _ => normalized
        };
    }

    private static bool ContainsText(Exception exception, string value)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsTypeName(Exception exception, string value)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool GetBooleanTag(Activity? activity, string tagName)
    {
        var value = activity?.GetTagItem(tagName);

        return value switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out var parsedValue) => parsedValue,
            _ => false
        };
    }

    private static int GetIntTag(Activity? activity, string tagName)
    {
        var value = activity?.GetTagItem(tagName);

        return value switch
        {
            int intValue => intValue,
            long longValue => checked((int)longValue),
            string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            _ => 0
        };
    }

    private static decimal? GetDecimalTag(Activity? activity, string tagName)
    {
        var value = activity?.GetTagItem(tagName);

        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            _ => null
        };
    }

    private static string? GetStringTag(Activity? activity, string tagName)
    {
        var value = activity?.GetTagItem(tagName);
        return value?.ToString();
    }

    private static string NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim().ToLowerInvariant();
    }
}

internal static class FaultInjectionSettings
{
    private const string EnabledVariable = "FAULT_INJECTION_ENABLED";
    private const string FailureStageVariable = "FAULT_INJECTION_FAIL_STAGE";
    private const string FailurePercentVariable = "FAULT_INJECTION_FAIL_PERCENT";
    private const string DelayStageVariable = "FAULT_INJECTION_DELAY_STAGE";
    private const string DelayPercentVariable = "FAULT_INJECTION_DELAY_PERCENT";
    private const string DelayMillisecondsVariable = "FAULT_INJECTION_DELAY_MS";

    public static CheckoutFaultInjectionSettings Load()
    {
        return new CheckoutFaultInjectionSettings(
            Enabled: ParseBoolean(EnabledVariable),
            FailureStage: NormalizeStage(Environment.GetEnvironmentVariable(FailureStageVariable)),
            FailurePercent: ParsePercent(FailurePercentVariable),
            DelayStage: NormalizeStage(Environment.GetEnvironmentVariable(DelayStageVariable)),
            DelayPercent: ParsePercent(DelayPercentVariable),
            DelayMilliseconds: ParseNonNegativeInt(DelayMillisecondsVariable));
    }

    private static bool ParseBoolean(string variableName)
    {
        return bool.TryParse(Environment.GetEnvironmentVariable(variableName), out var value) && value;
    }

    private static int ParsePercent(string variableName)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(variableName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }

    private static int ParseNonNegativeInt(string variableName)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(variableName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : 0;
    }

    private static string NormalizeStage(string? stage)
    {
        return string.IsNullOrWhiteSpace(stage) ? string.Empty : stage.Trim().ToLowerInvariant();
    }
}

internal readonly record struct CheckoutFaultInjectionSettings(
    bool Enabled,
    string FailureStage,
    int FailurePercent,
    string DelayStage,
    int DelayPercent,
    int DelayMilliseconds);

public readonly record struct CheckoutTelemetryContext(
    string Variant,
    int StoreId,
    int CartItemsCount,
    string PaymentMethodSystemName,
    bool IsRecurring,
    string OrderTotalRange);

public readonly record struct CheckoutStageResult(string Result, bool Success, string FailureCategory)
{
    public static CheckoutStageResult SuccessResult(string result = CheckoutResults.Success)
    {
        return new CheckoutStageResult(result, true, string.Empty);
    }

    public static CheckoutStageResult FailureResult(string failureCategory, string result = CheckoutResults.Failure)
    {
        return new CheckoutStageResult(result, false, failureCategory);
    }
}

public static class CheckoutStages
{
    public const string ConfirmOrder = "checkout.confirm_order";
    public const string PlaceOrder = "checkout.place_order";
    public const string PrepareDetails = "checkout.prepare_details";
    public const string PaymentProcess = "checkout.payment.process";
    public const string PaymentPostProcess = "checkout.payment.postprocess";
    public const string OrderSave = "checkout.order.save";
    public const string OrderItemsMove = "checkout.order_items.move";
    public const string InventoryAdjust = "checkout.inventory.adjust";
    public const string EventPublish = "checkout.event.publish";
}

public static class CheckoutVariants
{
    public const string Standard = "standard";
    public const string OnePage = "one_page";
}

public static class CheckoutResults
{
    public const string Success = "success";
    public const string Failure = "failure";
}

public static class FailureCategories
{
    public const string Validation = "validation";
    public const string PaymentDeclined = "payment_declined";
    public const string PaymentTimeout = "payment_timeout";
    public const string Inventory = "inventory";
    public const string Db = "db";
    public const string Unexpected = "unexpected";
}

public static class FaultTypes
{
    public const string Delay = "delay";
    public const string Failure = "failure";
}

public static class TelemetryTagNames
{
    public const string CartItemsCount = "cart.items.count";
    public const string CheckoutVariant = "checkout.variant";
    public const string ConsumerCount = "consumer.count";
    public const string ConsumerName = "consumer.name";
    public const string DbBatchSize = "db.batch.size";
    public const string DbEntityName = "db.nop.entity";
    public const string DbOperation = "db.operation";
    public const string DbSystem = "db.system";
    public const string Description = "description";
    public const string ErrorType = "error.type";
    public const string EventName = "event.name";
    public const string FaultDelayMilliseconds = "fault.delay.ms";
    public const string FaultInjected = "fault.injected";
    public const string FaultPercent = "fault.percent";
    public const string FaultStage = "fault.stage";
    public const string FaultType = "fault.type";
    public const string FailureCategory = "failure.category";
    public const string FailureStage = "failure.stage";
    public const string IsRecurring = "is_recurring";
    public const string OrderTotalRange = "order.total.range";
    public const string PaymentMethodSystemName = "payment.method.system_name";
    public const string Result = "result";
    public const string Stage = "stage";
    public const string StoreId = "store.id";
}
