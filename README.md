# 🛒 Retail Billing & Inventory Management System

A comprehensive, full-featured **retail billing and inventory management** desktop application built with **WPF (.NET 10)** and **SQLite**. Designed for small-to-medium retail businesses, it covers everything from GST-compliant invoicing and multi-store inventory to double-entry accounting, role-based access control, and automated backups.

---

## ✨ Key Features

### 📄 Invoicing & GST Compliance
- **GST invoices** (B2B/B2C) with CGST/SGST/IGST split and place-of-supply logic
- **Bill of Supply** and **Estimate** document types with one-click estimate → invoice conversion
- Tax-inclusive / exclusive pricing per line item with cess support
- Customizable invoice templates (A4, half-sheet, 80 mm thermal)
- Automatic numbering series with gap-free audit and FY rollover
- Print preview with laser and thermal printer support
- Share invoices via WhatsApp, SMS, and email

### 📦 Inventory & Stock Management
- Product catalog with categories, units, HSN codes, and batch/expiry tracking
- Real-time stock tracking with adjustments, write-offs, and stock-take workflows
- Low-stock and expiry alerts with configurable reorder policies
- Inter-store stock transfers
- Bulk CSV/Excel import for products and opening stock
- Price lists with customer/supplier-specific pricing and store-wise overrides

### 💰 Sales & Purchases
- Full sales register with customer selection and payment tracking
- Credit sales with FIFO settlement, credit limits, and ageing analysis
- Purchase management with supplier ledger
- Credit notes and returns processing
- Quick POS mode for fast retail billing

### 📊 Accounting & Finance
- Double-entry chart of accounts with auto-posting rules
- General journal with manual entries
- Cash book and ledger book views
- Bank reconciliation
- Profit & Loss, Balance Sheet, and Cash Flow statements
- Payables and Receivables ageing reports
- Financial year management with period close
- Cheque register with post-dated cheque lifecycle
- Tally XML export for accountant handoff

### 📈 Reports & Analytics
- Dashboard with sales trends, top products, and revenue charts (bar + donut)
- GST returns data (GSTR-1 / GSTR-3B ready)
- Item-wise profit analysis
- Stock valuation report
- Stock audit trail
- Store comparison analytics
- PDF and Excel report exports via QuestPDF and ClosedXML
- Scheduled report emails on a recurring timer

### 🏪 Multi-Store Support
- Multi-store configuration with store-wise pricing overrides
- Store comparison reports
- HQ sync center with live HTTPS or offline-first outbox queue

### 🔒 Security & Administration
- Role-based access control (RBAC) with granular permissions
- User management with SHA-256 password hashing
- SQLCipher database encryption at rest with DPAPI key file
- Passcode-protected critical operations (deletes, write-offs, period close)
- Full audit trail and activity logging
- Automated backup scheduler with catch-up runs
- Data integrity checks
- Privacy controls and data export

### 🔗 Integrations
- Unified contacts (customers + suppliers) with vCard/CSV support
- WhatsApp, SMS, and SMTP email messaging
- Tally XML export
- Reminder system with follow-up tracking

---

## 🏗 Architecture

```
RetailBillingApp/
├── Models/          # 60+ entity classes (EF Core)
├── Services/        # Interface-driven service layer (50+ services)
├── Data/            # AppDbContext + schema upgrader
├── ViewModels/      # MVVM view models (CommunityToolkit.Mvvm)
├── Views/           # 50+ WPF XAML views
├── Controls/        # Custom chart controls (bar, donut)
├── Converters/      # XAML value converters
├── Print/           # Invoice document builder & text renderer
└── Resources/       # App theme and styles
```

**Pattern:** MVVM with dependency injection via `Microsoft.Extensions.DependencyInjection`

---

## 🛠 Tech Stack

| Layer | Technology |
|-------|-----------|
| **Framework** | .NET 10 / WPF |
| **Language** | C# (latest, nullable enabled) |
| **ORM** | Entity Framework Core 10 (SQLite) |
| **Database** | SQLite + SQLCipher encryption |
| **MVVM** | CommunityToolkit.Mvvm 8.2.2 |
| **PDF Export** | QuestPDF 2026.9 (Community) |
| **Excel Export** | ClosedXML 0.105 |
| **Encryption** | SQLitePCLRaw.bundle_e_sqlcipher |

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or later)
- Windows 10/11

### Build & Run

```bash
# Clone the repository
git clone https://github.com/hydra-root/retail.git
cd retail

# Restore packages and build
dotnet restore
dotnet build

# Run the application
dotnet run
```

### Default Login

| Username | Password |
|----------|----------|
| `admin` | `admin123` |

> ⚠️ **Change the default password** after first login.

---

## 📦 NuGet Packages

| Package | Purpose |
|---------|---------|
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite database provider |
| `Microsoft.Data.Sqlite.Core` | Low-level SQLite access |
| `SQLitePCLRaw.bundle_e_sqlcipher` | Database encryption at rest |
| `CommunityToolkit.Mvvm` | MVVM source generators & base classes |
| `QuestPDF` | PDF invoice/report generation |
| `ClosedXML` | Excel report export |

---

## 📊 Project Status

**89% complete** — 80 of 90 planned features implemented and functional.

See [`PROGRESS_TRACKING.md`](PROGRESS_TRACKING.md) for the full feature checklist and build status.

---

## 📄 License

This project is proprietary. All rights reserved.

---

## 🤝 Contributing

This is a private project. Please contact the repository owner for contribution guidelines.
