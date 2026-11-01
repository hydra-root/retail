using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace RetailBillingApp.Data;

/// <summary>
/// Lightweight schema upgrade for existing app.db files.
/// EnsureCreated only creates a *new* database; it never alters an existing one.
/// This applies CREATE TABLE IF NOT EXISTS / ALTER TABLE ADD COLUMN for tables
/// and columns introduced after the first release, so users never have to
/// manually delete the database.
/// </summary>
public static class DatabaseSchemaUpgrader
{
    public static void Apply(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();

        try
        {
            // --- New tables ---
            Execute(connection, @"
                CREATE TABLE IF NOT EXISTS Categories (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    ParentCategoryId INTEGER NULL,
                    IsActive INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Name ON Categories (Name);

                CREATE TABLE IF NOT EXISTS Units (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Symbol TEXT NOT NULL,
                    BaseUnitId INTEGER NULL,
                    Factor TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Units_Name ON Units (Name);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Units_Symbol ON Units (Symbol);

                CREATE TABLE IF NOT EXISTS ProductBatches (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    BatchNumber TEXT NOT NULL,
                    ManufacturingDate TEXT NULL,
                    ExpiryDate TEXT NULL,
                    MRP TEXT NOT NULL,
                    PurchaseRate TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    StoreId INTEGER NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductBatches_ProductId_BatchNumber
                    ON ProductBatches (ProductId, BatchNumber);

                CREATE TABLE IF NOT EXISTS AuditRecords (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    EntityName TEXT NOT NULL,
                    EntityId INTEGER NOT NULL,
                    EntityLabel TEXT NULL,
                    ChangesJson TEXT NULL,
                    SnapshotJson TEXT NULL,
                    Timestamp TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AuditRecords_Timestamp ON AuditRecords (Timestamp);
                CREATE INDEX IF NOT EXISTS IX_AuditRecords_EntityName ON AuditRecords (EntityName);
                CREATE INDEX IF NOT EXISTS IX_AuditRecords_Username ON AuditRecords (Username);

                CREATE TABLE IF NOT EXISTS BankStatementLines (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ValueDate TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    ReferenceNo TEXT NULL,
                    Amount TEXT NOT NULL,
                    MatchedType TEXT NOT NULL,
                    MatchedRef TEXT NULL,
                    Status TEXT NOT NULL,
                    ImportedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_BankStatementLines_ValueDate ON BankStatementLines (ValueDate);
                CREATE INDEX IF NOT EXISTS IX_BankStatementLines_Status ON BankStatementLines (Status);

                CREATE TABLE IF NOT EXISTS StockTakes (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    StockTakeNumber TEXT NOT NULL,
                    StockTakeDate TEXT NOT NULL,
                    StoreId INTEGER NULL,
                    Status TEXT NOT NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_StockTakes_StockTakeDate ON StockTakes (StockTakeDate);

                CREATE TABLE IF NOT EXISTS StockTakeItems (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    StockTakeId INTEGER NOT NULL,
                    ProductId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL,
                    SystemQty INTEGER NOT NULL,
                    CountedQty INTEGER NULL,
                    Saved INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_StockTakeItems_StockTakeId ON StockTakeItems (StockTakeId);

                CREATE TABLE IF NOT EXISTS StockTransfers (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TransferNumber TEXT NOT NULL,
                    TransferDate TEXT NOT NULL,
                    FromStoreId INTEGER NOT NULL,
                    ToStoreId INTEGER NOT NULL,
                    ProductId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Rate TEXT NOT NULL,
                    TransferValue TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    ReceivedDate TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_StockTransfers_Status ON StockTransfers (Status);
                CREATE INDEX IF NOT EXISTS IX_StockTransfers_TransferDate ON StockTransfers (TransferDate);

                CREATE TABLE IF NOT EXISTS MessagingSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    SmtpHost TEXT NULL,
                    SmtpPort INTEGER NOT NULL DEFAULT 587,
                    SmtpUser TEXT NULL,
                    SmtpPassword TEXT NULL,
                    FromAddress TEXT NULL,
                    DltSmsSender TEXT NULL,
                    DltTemplateId TEXT NULL,
                    DefaultWhatsAppCountryCode TEXT NOT NULL DEFAULT '91'
                );

                CREATE TABLE IF NOT EXISTS MessagingLogs (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    Recipient TEXT NULL,
                    Reference TEXT NULL,
                    Status TEXT NOT NULL,
                    Detail TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_MessagingLogs_Timestamp ON MessagingLogs (Timestamp);
                CREATE INDEX IF NOT EXISTS IX_MessagingLogs_Channel ON MessagingLogs (Channel);

                CREATE TABLE IF NOT EXISTS InvoiceTemplateSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TemplateName TEXT NOT NULL,
                    LogoPath TEXT NULL,
                    ShowLogo INTEGER NOT NULL,
                    HeaderText TEXT NULL,
                    FooterText TEXT NULL,
                    TermsText TEXT NULL,
                    ShowTaxBreakup INTEGER NOT NULL,
                    ShowSignatureLine INTEGER NOT NULL,
                    SignatureCaption TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS StockPolicySettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    NegativeStockMode TEXT NOT NULL DEFAULT 'Block',
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS PaymentSplits (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    PaymentId INTEGER NOT NULL,
                    Mode TEXT NOT NULL,
                    Amount TEXT NOT NULL,
                    Reference TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PaymentSplits_PaymentId ON PaymentSplits (PaymentId);

                CREATE TABLE IF NOT EXISTS BackupScheduleSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsEnabled INTEGER NOT NULL DEFAULT 0,
                    Frequency TEXT NOT NULL DEFAULT 'Daily',
                    IntervalDays INTEGER NOT NULL DEFAULT 1,
                    DayOfWeek TEXT NOT NULL DEFAULT 'Sunday',
                    RunAtHour INTEGER NOT NULL DEFAULT 21,
                    RunAtMinute INTEGER NOT NULL DEFAULT 30,
                    DestinationFolder TEXT NULL,
                    KeepGenerations INTEGER NOT NULL DEFAULT 14,
                    IncludeManualBackupsInPrune INTEGER NOT NULL DEFAULT 0,
                    LastRunAt TEXT NULL,
                    LastResult TEXT NULL,
                    NextRunAt TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS ReminderSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsEnabled INTEGER NOT NULL DEFAULT 0,
                    FirstNoticeDaysBeforeDue INTEGER NOT NULL DEFAULT 3,
                    SecondNoticeDaysAfterDue INTEGER NOT NULL DEFAULT 7,
                    Channel TEXT NOT NULL DEFAULT 'WhatsApp',
                    MessageTemplate TEXT NOT NULL,
                    MaxPerCustomerPerDay INTEGER NOT NULL DEFAULT 1,
                    LastRunAt TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS CreditSaleSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    DefaultCreditDays INTEGER NOT NULL DEFAULT 30,
                    GraceDays INTEGER NOT NULL DEFAULT 3,
                    BlockWhenOverLimit INTEGER NOT NULL DEFAULT 1,
                    RequireAdminApprovalOverLimit INTEGER NOT NULL DEFAULT 1,
                    MaxAgeingDaysBeforeBlocked INTEGER NOT NULL DEFAULT 0,
                    AppliesToCustomerGroup TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS CriticalChangeSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsEnabled INTEGER NOT NULL DEFAULT 0,
                    PasscodeHash TEXT NULL,
                    RequireForInvoiceDelete INTEGER NOT NULL DEFAULT 1,
                    RequireForWriteOff INTEGER NOT NULL DEFAULT 1,
                    RequireForEstimateConversion INTEGER NOT NULL DEFAULT 0,
                    RequireForLockedPeriodEdit INTEGER NOT NULL DEFAULT 1,
                    RequireForStockAdjustment INTEGER NOT NULL DEFAULT 0,
                    RequireForFinancialYearLock INTEGER NOT NULL DEFAULT 1,
                    MaxFailedAttempts INTEGER NOT NULL DEFAULT 5,
                    LockoutMinutes INTEGER NOT NULL DEFAULT 15,
                    FailedAttempts INTEGER NOT NULL DEFAULT 0,
                    LockoutUntil TEXT NULL,
                    LastChangedAt TEXT NULL,
                    LastChangedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS OfflineSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsForcedOffline INTEGER NOT NULL DEFAULT 0,
                    AutoSyncEnabled INTEGER NOT NULL DEFAULT 1,
                    AutoSyncMinutes INTEGER NOT NULL DEFAULT 15,
                    MaxAttempts INTEGER NOT NULL DEFAULT 5,
                    LastSyncAt TEXT NULL,
                    LastResult TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS PendingChanges (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    EntityType TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    EntityKey TEXT NOT NULL,
                    StoreId INTEGER NULL,
                    PayloadJson TEXT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    AttemptedAt TEXT NULL,
                    SyncedAt TEXT NULL,
                    Attempts INTEGER NOT NULL DEFAULT 0,
                    LastError TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS HoldTickets (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    HoldLabel TEXT NOT NULL,
                    HeldAt TEXT NOT NULL,
                    CustomerId INTEGER NULL,
                    CustomerName TEXT NULL,
                    SubTotal TEXT NOT NULL,
                    TaxAmount TEXT NOT NULL,
                    TotalAmount TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_HoldTickets_HeldAt ON HoldTickets (HeldAt);

                CREATE TABLE IF NOT EXISTS HoldTicketItems (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    HoldTicketId INTEGER NOT NULL,
                    ProductId INTEGER NULL,
                    ProductName TEXT NOT NULL,
                    HSNCode TEXT NULL,
                    Quantity INTEGER NOT NULL,
                    Rate TEXT NOT NULL,
                    TaxPercentage TEXT NOT NULL,
                    CessRate TEXT NOT NULL,
                    TaxInclusive INTEGER NOT NULL,
                    Amount TEXT NOT NULL,
                    Total TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_HoldTicketItems_HoldTicketId ON HoldTicketItems (HoldTicketId);

                CREATE TABLE IF NOT EXISTS PriceLists (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    CustomerGroup TEXT NULL,
                    CustomerId INTEGER NULL,
                    ValidFrom TEXT NOT NULL,
                    ValidTo TEXT NULL,
                    IsActive INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PriceLists_CustomerId ON PriceLists (CustomerId);

                CREATE TABLE IF NOT EXISTS PriceListItems (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    PriceListId INTEGER NOT NULL,
                    ProductId INTEGER NOT NULL,
                    Rate TEXT NOT NULL,
                    DiscountPct TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PriceListItems_PriceListId ON PriceListItems (PriceListId);
                CREATE INDEX IF NOT EXISTS IX_PriceListItems_ProductId ON PriceListItems (ProductId);

                CREATE TABLE IF NOT EXISTS Cheques (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Direction TEXT NOT NULL,
                    PartyType TEXT NOT NULL,
                    CustomerId INTEGER NULL,
                    SupplierId INTEGER NULL,
                    PartyName TEXT NOT NULL,
                    ChequeNumber TEXT NOT NULL,
                    BankName TEXT NULL,
                    IssueDate TEXT NOT NULL,
                    PresentmentDate TEXT NOT NULL,
                    Amount TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    ClearedDate TEXT NULL,
                    BouncedDate TEXT NULL,
                    BounceReason TEXT NULL,
                    CreateReceiptOnClear INTEGER NOT NULL,
                    PaymentId INTEGER NULL,
                    PaymentNumber TEXT NULL,
                    StoreId INTEGER NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Cheques_Status ON Cheques (Status);
                CREATE INDEX IF NOT EXISTS IX_Cheques_PresentmentDate ON Cheques (PresentmentDate);

                CREATE TABLE IF NOT EXISTS SyncSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsEnabled INTEGER NOT NULL,
                    BaseUrl TEXT NOT NULL,
                    ApiKey TEXT NOT NULL,
                    DeviceIdentifier TEXT NOT NULL,
                    UseTlsOnly INTEGER NOT NULL,
                    BatchSize INTEGER NOT NULL,
                    TimeoutSeconds INTEGER NOT NULL,
                    LastSyncAt TEXT NULL,
                    LastResult TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS SyncRunLogs (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    StartedAt TEXT NOT NULL,
                    FinishedAt TEXT NULL,
                    Trigger TEXT NOT NULL,
                    TransportName TEXT NOT NULL,
                    AttemptedCount INTEGER NOT NULL,
                    AcceptedCount INTEGER NOT NULL,
                    RejectedCount INTEGER NOT NULL,
                    FailedCount INTEGER NOT NULL,
                    RemainingCount INTEGER NOT NULL,
                    Outcome TEXT NOT NULL,
                    Error TEXT NULL,
                    StoreId INTEGER NULL,
                    DeviceIdentifier TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_SyncRunLogs_StartedAt ON SyncRunLogs (StartedAt);

                CREATE TABLE IF NOT EXISTS StorePriceOverrides (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    StoreId INTEGER NOT NULL,
                    ProductId INTEGER NOT NULL,
                    CustomerId INTEGER NULL,
                    CustomerGroup TEXT NULL,
                    Rate TEXT NOT NULL,
                    DiscountPct TEXT NOT NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_StorePriceOverrides_StoreId ON StorePriceOverrides (StoreId);
                CREATE INDEX IF NOT EXISTS IX_StorePriceOverrides_ProductId ON StorePriceOverrides (ProductId);

                CREATE TABLE IF NOT EXISTS ScheduledReportSettings (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IsEnabled INTEGER NOT NULL,
                    ReportKey TEXT NOT NULL,
                    ReportTitle TEXT NOT NULL,
                    RecipientEmail TEXT NOT NULL,
                    Subject TEXT NOT NULL,
                    RunAtHour INTEGER NOT NULL,
                    RunAtMinute INTEGER NOT NULL,
                    DayOfWeek TEXT NOT NULL,
                    LastRunAt TEXT NULL,
                    LastResult TEXT NULL,
                    NextRunAt TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UpdatedBy TEXT NULL
                );
            ");

            // --- New columns on existing tables ---
            AddColumnIfMissing(connection, "Products", "Brand", "TEXT");
            AddColumnIfMissing(connection, "Products", "CategoryId", "INTEGER");
            AddColumnIfMissing(connection, "Products", "UnitId", "INTEGER");
            AddColumnIfMissing(connection, "ProductBatches", "StoreId", "INTEGER");
            AddColumnIfMissing(connection, "Invoices", "IsBillOfSupply", "INTEGER");
            AddColumnIfMissing(connection, "Invoices", "IsEstimate", "INTEGER");
            AddColumnIfMissing(connection, "Invoices", "WriteOffAmount", "TEXT");
            AddColumnIfMissing(connection, "Customers", "GSTIN", "TEXT");
            AddColumnIfMissing(connection, "Customers", "State", "TEXT");
            AddColumnIfMissing(connection, "Customers", "CustomerGroup", "TEXT");
            // PAY-08: background reminder auto-run (armed from the backup scheduler tick)
            AddColumnIfMissing(connection, "ReminderSettings", "AutoRunEnabled", "INTEGER");
            AddColumnIfMissing(connection, "ReminderSettings", "AutoRunHour", "INTEGER");
            AddColumnIfMissing(connection, "ReminderSettings", "AutoRunMinute", "INTEGER");
            AddColumnIfMissing(connection, "ReminderSettings", "LastAutoRunAt", "TEXT");
            AddColumnIfMissing(connection, "ReminderSettings", "LastAutoResult", "TEXT");
            // Backfill so non-nullable model props never read NULL from pre-upgrade rows.
            Execute(connection, "UPDATE ReminderSettings SET AutoRunEnabled = 0 WHERE AutoRunEnabled IS NULL");
            Execute(connection, "UPDATE ReminderSettings SET AutoRunHour = 9 WHERE AutoRunHour IS NULL");
            Execute(connection, "UPDATE ReminderSettings SET AutoRunMinute = 0 WHERE AutoRunMinute IS NULL");
        }
        finally
        {
            connection.Close();
        }
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(DbConnection connection, string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        var exists = Convert.ToInt32(check.ExecuteScalar()) > 0;
        if (!exists)
        {
            Execute(connection, $"ALTER TABLE {table} ADD COLUMN {column} {type} NULL");
        }
    }
}
