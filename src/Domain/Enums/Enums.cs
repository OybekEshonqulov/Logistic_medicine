namespace PharmaTrack.Domain.Enums;

public enum UserRole { Admin, Warehouse, Executive, Dealer }

public enum DistrictType { District, City }

public enum LocationType { Central, Dealer }

public enum PharmacyStatus { Pending, Active, Rejected }

public enum ShipmentStatus { Draft, Approved, InTransit, Received, Cancelled }

public enum DiscrepancyStatus { None, Open, Resolved }

public enum DiscrepancyResolution
{
    /// <summary>The sender's stock was wrong: apply the difference to the sender's balance.</summary>
    CorrectSender,
    /// <summary>The difference is accepted as a loss or surplus; no stock changes.</summary>
    AcceptAsIs
}

public enum SaleStatus { Active, Reversed }

public enum StockCountStatus { InProgress, Completed }

public enum AdjustmentType { WriteOffExpired, WriteOffDamaged, WriteOffOther, PharmacyReturn }

public enum AdjustmentStatus { Pending, Approved, Rejected }

public enum MovementType
{
    Receipt,
    ShipmentOut,
    ShipmentIn,
    ShipmentCorrection,
    Sale,
    SaleReversal,
    CountAdjustment,
    WriteOff,
    PharmacyReturn
}

public enum NotificationType { LowStock, ExpiringBatch, ShipmentNotReceived, SalesNotReported }
