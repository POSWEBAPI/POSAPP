using iText.StyledXmlParser.Jsoup.Helper;
using NuGet.Protocol.Plugins;
using System;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

public class DatabaseInitializer
{
    public static void Initialize()
    {
        // ✅ FIX: Proper DB path
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShriPOS.db");

        // ✅ Ensure directory exists
        Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory);

        string connectionString = $"Data Source={dbPath};Version=3;";

        using (var conn = new SQLiteConnection(connectionString))
        {
            conn.Open();

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS CurrencyMaster (
                CurrencyID INTEGER PRIMARY KEY AUTOINCREMENT,
                CurrencyName TEXT NOT NULL,
                CurrencySymbol TEXT NOT NULL,
                Status INTEGER NOT NULL,
                CompanyID INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS CompanyMaster (
                CompanyID INTEGER PRIMARY KEY AUTOINCREMENT,
                CompanyCode TEXT NOT NULL,
                Name TEXT NOT NULL,
                Address TEXT,
                City TEXT,
                Country TEXT,
                Mobile TEXT,
                Email TEXT,
                Website TEXT,
                Logo BLOB,
                BaseCurrency INTEGER NOT NULL,
                DefaultCostMethod TEXT NOT NULL,
                ContactPerson TEXT,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS CompanyDeliveryAddress (
                CompanyDeliveryAddID INTEGER PRIMARY KEY AUTOINCREMENT,
                CompanyID INTEGER,
                Address TEXT,
                City TEXT,
                Country TEXT,
                ContactPerson TEXT,
                Mobile TEXT,
                Type TEXT,
                DefaultFlag INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS Store (
    StoreID INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyID INTEGER,
    StoreCode TEXT NOT NULL,
    StoreName TEXT NOT NULL,
    Address TEXT,
    City TEXT,
    Country TEXT,
    CurrencyID INTEGER,  -- Changed from Currency to CurrencyID
    Status INTEGER,
    CreatedBy INTEGER NOT NULL,
    CreatedDate TEXT NOT NULL,
    ModifiedBy INTEGER,
    ModifiedDate TEXT
);");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS Terminal (
                TerminalID INTEGER PRIMARY KEY AUTOINCREMENT,
                StoreID INTEGER,
                Code TEXT NOT NULL,
                OfflineAllowed INTEGER,
                Status INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS UOM (
                UOMID INTEGER PRIMARY KEY AUTOINCREMENT,
                UOMDescription TEXT,
                Status INTEGER,
                CompanyID INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS Department (
                DepartmentID INTEGER PRIMARY KEY AUTOINCREMENT,
                DepartmentName TEXT NOT NULL,
                Status INTEGER,
                CompanyID INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS Category (
                CategoryID INTEGER PRIMARY KEY AUTOINCREMENT,
                CategoryName TEXT NOT NULL,
                CompanyID INTEGER,
                Status INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS Charges (
                ChargesID INTEGER PRIMARY KEY AUTOINCREMENT,
                ChargesName TEXT NOT NULL,
                CompanyID INTEGER,
                Status INTEGER,
                ChargeType INTEGER,
                IncludeInItem INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS SubCategory (
                SubCategoryID INTEGER PRIMARY KEY AUTOINCREMENT,
                SubCategoryName TEXT NOT NULL,
                CompanyID INTEGER,
                Status INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS Tax (
                TaxID INTEGER PRIMARY KEY AUTOINCREMENT,
                TaxCode TEXT NOT NULL,
                TaxDescription TEXT,
                TaxPercent REAL,
                FromDate TEXT,
                ToDate TEXT,
                CompanyID INTEGER,
                Status INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS Discount (
                DiscountID INTEGER PRIMARY KEY AUTOINCREMENT,
                DiscountCode TEXT NOT NULL,
                Description TEXT,
                DiscountPercentage REAL,
                FromDate TEXT,
                ToDate TEXT,
                PurchaseSales INTEGER,
                CompanyID INTEGER,
                Status INTEGER,
                CreatedBy INTEGER NOT NULL,
                CreatedDate TEXT NOT NULL,
                ModifiedBy INTEGER,
                ModifiedDate TEXT
            );");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS Item (
    ItemID INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyID INTEGER,
    SKU TEXT NOT NULL,
    ItemName TEXT NOT NULL,
    BaseUOM INTEGER,
    DepartmentID INTEGER,   -- Changed from Department
    CategoryID INTEGER,     -- Changed from Category
    SubCategoryID INTEGER,  -- Changed from SubCategory
    IsPackSizeEnabled INTEGER,
    IsBatchEnabled INTEGER,
    IsSerialNoEnabled INTEGER,
    IsLotNoEnabled INTEGER,
    CostPrice REAL,
    SellingPrice REAL,
    PurchaseTaxID INTEGER,  -- Changed from PurchaseTax
    SalesTaxID INTEGER,     -- Changed from SalesTax
    BarCode TEXT,
    Status INTEGER,
    CreatedBy INTEGER NOT NULL,
    CreatedDate TEXT NOT NULL,
    ModifiedBy INTEGER,
    ModifiedDate TEXT
);");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS CustomerMaster (
    CustomerID INTEGER PRIMARY KEY AUTOINCREMENT, -- AutoGenerated PK
    CustomerCode TEXT NOT NULL,                   -- Varchar(5)
    CustomerName TEXT NOT NULL,                   -- Varchar(200)
    Address TEXT,                                 -- Varchar(Max)
    City TEXT,                                    -- Varchar(100)
    Country TEXT,                                 -- Varchar(100)
    Mobile TEXT,                                  -- Varchar(100)
    Email TEXT,                                   -- Varchar(100)
    Website TEXT,                                 -- Varchar(100)
    Logo BLOB,                                    -- BLOB for Upload Logo
    CurrencyID INTEGER,                           -- FK from Currency Master
    ContactPerson TEXT,                           -- Varchar(100)
    ImportExport INTEGER,                         -- Bit (Yes/No) stored as 0 or 1
    CompanyID INTEGER,                            -- FK from Company Master
    Status INTEGER,                               -- Bit (Yes/No) stored as 0 or 1
    CreatedBy INTEGER NOT NULL,                   -- Internal Column
    CreatedDate TEXT NOT NULL,                    -- Internal Column
    ModifiedBy INTEGER,                           -- Internal Column
    ModifiedDate TEXT                             -- Internal Column
);");

            Execute(conn, @"
                CREATE TABLE IF NOT EXISTS PaymentMethod (
                    PaymentMethodID      INTEGER PRIMARY KEY AUTOINCREMENT,
                    PayMethodShort       TEXT NOT NULL,
                    PayMethodDescription TEXT NOT NULL,
                    CurrencyID           INTEGER NOT NULL,
                    Status               INTEGER NOT NULL DEFAULT 1,
                    CreatedBy            INTEGER NOT NULL,
                    CreatedDate          TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ModifiedBy           INTEGER,
                    ModifiedDate         TEXT
                );
            ");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS POSPermission (
    RoleID INTEGER PRIMARY KEY,
    RoleName TEXT,
    VoidLine INTEGER,
    VoidTransaction INTEGER,
    Discount INTEGER,
    PriceOverride INTEGER,
    MaxDiscountPercent INTEGER,
    TotalDiscountPercent INTEGER,
    CreatedBy INTEGER NOT NULL,
    CreatedDate TEXT NOT NULL,
    ModifiedBy INTEGER,
    ModifiedDate TEXT
);");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS Users (
    PKuserID INTEGER PRIMARY KEY AUTOINCREMENT,
    UserID INTEGER NOT NULL UNIQUE,           -- 5 digit unique user ID
    Password TEXT NOT NULL,                   -- Encrypted Password
    Email TEXT NOT NULL,
    Mobile TEXT,
    RoleID INTEGER,
    StoreID INTEGER,
    CompanyID INTEGER,
    Status INTEGER,
    CreatedBy INTEGER NOT NULL,
    CreatedDate TEXT NOT NULL,
    ModifiedBy INTEGER,
    ModifiedDate TEXT
);");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS StockMovement (
    StockMovementId  INTEGER PRIMARY KEY,
    ItemID           INTEGER,
    CompanyID        INTEGER,
    StoreID          INTEGER,
    MovementType     INTEGER,
    TransactionID    INTEGER,
    CreatedUTC       TEXT,
    ItemQty          REAL,
    ItemCode         TEXT
);");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS D365Products (
    ItemId              TEXT PRIMARY KEY,
    NameAlias           TEXT,
    AvailPhysical       REAL,
    InventLocationId    TEXT,
    Amount              REAL,
    InventSiteId        TEXT,
    WMSLocationId       TEXT,
    AccountRelation     TEXT,
    OnHandModifiedDateTime TEXT,
    LastUpdatedLocal    TEXT
);");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS POS_SyncControl (
    SyncType            TEXT PRIMARY KEY,
    LastSyncDateTime    TEXT NOT NULL
);");

            Execute(conn, @"
            CREATE TABLE IF NOT EXISTS tender_declaration(
                tender_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                shift_id        INTEGER NOT NULL,
                confirm_user_id INTEGER,
                notes_200       INTEGER DEFAULT 0,
                notes_100       INTEGER DEFAULT 0,
                notes_50        INTEGER DEFAULT 0,
                notes_20        INTEGER DEFAULT 0,
                notes_10        INTEGER DEFAULT 0,
                coins_5         INTEGER DEFAULT 0,
                coins_2         INTEGER DEFAULT 0,
                coins_1         INTEGER DEFAULT 0,
                coins_50c       INTEGER DEFAULT 0,
                coins_20c       INTEGER DEFAULT 0,
                coins_10c       INTEGER DEFAULT 0,
                cash_counted    REAL DEFAULT 0.00,
                upi_amount      REAL DEFAULT 0.00,
                card_amount     REAL DEFAULT 0.00,
                bank_transfer   REAL DEFAULT 0.00,
                grand_total     REAL DEFAULT 0.00,
                confirmed       TEXT DEFAULT 'Y' CHECK(confirmed IN('Y', 'N')),
                created_at      DATETIME DEFAULT CURRENT_TIMESTAMP,
                updated_at      DATETIME DEFAULT CURRENT_TIMESTAMP
            );");

            Execute(conn, @"
           CREATE TABLE IF NOT EXISTS PendingInvoice(
                    InvoiceNo     TEXT PRIMARY KEY,
                    CustomerName  TEXT,
                    SaleDate      TEXT,
                    GrandTotal    REAL,
                    Status        TEXT DEFAULT 'Unpaid',
                    CartJson      TEXT,
                    CompanyID     INTEGER
               );");
            Execute(conn, @"
          CREATE TABLE IF NOT EXISTS CustomerCache (
    CompanyID     INTEGER NOT NULL,
    CustomerID    INTEGER NOT NULL,
    CustomerCode  TEXT,
    CustomerName  TEXT,
    Address       TEXT,
    City          TEXT,
    Country       TEXT,
    Mobile        TEXT,
    Email         TEXT,
    Status        INTEGER,
    LastSyncUtc   TEXT,
    PRIMARY KEY (CompanyID, CustomerID)
);");
        }
    }

    private static void Execute(SQLiteConnection conn, string query)
    {
        using (var cmd = new SQLiteCommand(query, conn))
        {
            cmd.ExecuteNonQuery();
        }
    }
}