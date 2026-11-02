namespace PharmaTrack.Domain.Common;

public static class ErrorCodes
{
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string InvalidRefreshToken = "auth.invalid_refresh_token";
    public const string UserInactive = "auth.user_inactive";
    public const string WrongPassword = "auth.wrong_password";
    public const string Unauthorized = "common.unauthorized";
    public const string Forbidden = "common.forbidden";
    public const string NotFound = "common.not_found";
    public const string Conflict = "common.conflict";
    public const string ValidationFailed = "common.validation_failed";
    public const string InternalError = "common.internal_error";
    public const string TooManyRequests = "common.too_many_requests";
    public const string IdempotencyKeyRequired = "common.idempotency_key_required";
    public const string PhoneTaken = "user.phone_taken";
    public const string DealerRequired = "user.dealer_required";
    public const string RegionMustBeDistrict = "region.must_be_district";
    public const string DistrictNotInRegion = "district.not_in_region";
    public const string BatchExpiryMismatch = "batch.expiry_mismatch";
    public const string BatchExpired = "batch.expired";
    public const string InsufficientStock = "stock.insufficient";
    public const string InvalidShipmentStatus = "shipment.invalid_status";
    public const string InvalidShipmentRoute = "shipment.invalid_route";
    public const string DiscrepancyNotOpen = "shipment.discrepancy_not_open";
    public const string ReceiveItemsMismatch = "shipment.receive_items_mismatch";
    public const string PharmacyNotAvailable = "pharmacy.not_available";
    public const string PharmacyNotPending = "pharmacy.not_pending";
    public const string SaleAlreadyReversed = "sale.already_reversed";
    public const string SaleDateInFuture = "sale.date_in_future";
    public const string CountNotInProgress = "stock_count.not_in_progress";
    public const string CountItemsMismatch = "stock_count.items_mismatch";
    public const string AdjustmentNotPending = "adjustment.not_pending";
    public const string ImportFailed = "import.failed";
    public const string UnknownReport = "export.unknown_report";
}
