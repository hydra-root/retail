# Retail Billing & Inventory Management System

A Windows desktop retail management application built with WPF, .NET 10, MVVM, EF Core, and encrypted SQLite. The project is designed for small-to-mid-sized retail and wholesale businesses that need invoice generation, stock tracking, customer and supplier management, accounting workflows, and operational controls in a single desktop application.

This repository includes a complete desktop billing workflow with inventory management, GST-aware invoicing, payment handling, credit controls, stock movement tracking, financial reporting, and role-based authorization.

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Application Architecture](#application-architecture)
- [Project Structure](#project-structure)
- [Technology Stack](#technology-stack)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Running the Application](#running-the-application)
- [Database and Security](#database-and-security)
- [Default Login](#default-login)
- [Functional Modules](#functional-modules)
- [Known Notes](#known-notes)
- [License](#license)

## Overview

The application is organized around a layered MVVM design:

- `Views/` contains WPF windows and user controls
- `ViewModels/` contains the presentation logic and bindings
- `Services/` contains business logic for billing, stock, accounting, and reporting
- `Models/` contains the main domain entities
- `Data/` contains EF Core configuration and schema upgrade logic

The application is intended to run on Windows and is optimized for business workflows commonly used in Indian retail operations, including GST rules, invoice numbering, tax calculation, stock-ledger controls, and store-wise operations.

## Key Features

### Billing and invoicing
- Create sales invoices with customer selection, item selection, pricing, and quantity handling
- Support GST-aware invoice calculations and invoice type variations
- Handle invoice numbering with number-series management
- Support estimates, credit notes, and invoice conversions
- Maintain invoice status, payment tracking, and write-off handling
- Support print preview and printing for invoice layouts

### Inventory management
- Product catalog with SKU, barcode, category, unit, and stock tracking
- Batch and expiry tracking for goods
- Stock transactions and stock movement records
- Stock adjustments, transfers, and policy enforcement
- Stock audits and stock take records

### Financial and accounting features
- Journal entries and financial posting
- Accounts and account groups
- Cash book and bank reconciliation workflows
- Payment handling and allocations
- Sales, receivables, payables, and ledger views

### Customer and supplier operations
- Customer master data with credit management
- Supplier and purchase order workflows
- Credit sale rules and exposure tracking
- Party-wise account activity and ledger reporting

### Reporting and analytics
- Dashboard views
- Sales and financial report pages
- Ledger, stock valuation, item profit, and cash flow views
- Scheduled report and export capabilities

### Security and administration
- Role-based authentication and authorization
- Admin/user login flow
- Activity logging and audit trail
- Critical operation guard flow and override checks
- Application-level encryption and secure database setup

## Application Architecture

The application uses a standard layered pattern:

```text
WPF Views
   ↓
MVVM ViewModels
   ↓
Services / Business Logic
   ↓
EF Core DbContext + SQLite/SQLCipher
   ↓
Encrypted database file (app.db)
```

At startup, the app configures dependency injection, selects the UI theme, applies localization, and initializes the database.

The `App.xaml.cs` file is the composition root for the desktop app and registers the main services used across the solution.

## Project Structure

```text
RetailBillingApp/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── RetailBillingApp.csproj
├── RetailBillingApp.slnx
├── README.md
├── app.db
├── app.db.key
├── crash.txt
├── Data/
│   ├── AppDbContext.cs
│   └── DatabaseSchemaUpgrader.cs
├── Models/
│   ├── Account.cs
│   ├── Customer.cs
│   ├── Invoice.cs
│   ├── Product.cs
│   ├── Store.cs
│   ├── User.cs
│   └── ...
├── Services/
│   ├── AuthService.cs
│   ├── InvoiceService.cs
│   ├── ProductService.cs
│   ├── PaymentService.cs
│   ├── SaleService.cs
│   ├── ReportService.cs
│   ├── BackupService.cs
│   └── ...
├── ViewModels/
│   ├── DashboardViewModel.cs
│   ├── InvoicesViewModel.cs
│   ├── ProductsViewModel.cs
│   ├── SalesViewModel.cs
│   ├── LoginViewModel.cs
│   └── ...
├── Views/
│   ├── LoginView.xaml
│   ├── DashboardView.xaml
│   ├── InvoicesView.xaml
│   ├── ProductsView.xaml
│   ├── ReportsView.xaml
│   └── ...
├── Resources/
│   └── Styles/
├── Converters/
├── bin/
├── obj/
└── graphify-out/
```

## Technology Stack

- .NET 10 WPF desktop application
- C# / XAML
- Microsoft.Extensions.DependencyInjection
- CommunityToolkit.Mvvm
- Entity Framework Core
- SQLite with SQLCipher encryption
- ClosedXML for Excel workflows
- QuestPDF for PDF/report generation
- QRCoder for QR code generation
- MaterialDesignThemes for WPF styling
- Npgsql.EntityFrameworkCore.PostgreSQL for compatibility with PostgreSQL use-cases

## Prerequisites

Before running the project, install:

- Windows 10 or later
- .NET 10 SDK
- Visual Studio 2022 with .NET desktop development workload
- Optional: Git for cloning

If the project is built from the command line, ensure the .NET SDK is available in PATH.

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/hydra-root/retail.git
cd retail
```

### 2. Restore dependencies

```bash
dotnet restore
```

### 3. Build the solution

```bash
dotnet build
```

### 4. Run the app

```bash
dotnet run
```

If you are using Visual Studio, open `RetailBillingApp.slnx` and run the project in Debug mode.

## Running the Application

On first start, the app initializes its data layer and creates or upgrades the local SQLite database. The app uses the encrypted database connection configured in the startup path, and the database file is created in the project directory unless redirected by configuration.

The application will start with the login screen.

## Database and Security

The project stores its main data in a SQLite database using SQLCipher-backed encryption settings. The codebase includes database upgrade logic and migration-friendly schema creation patterns.

Relevant implementation points:

- `Data/AppDbContext.cs` defines the EF Core model and DbSet registrations
- `Data/DatabaseSchemaUpgrader.cs` applies schema updates for an existing database
- `Services/EncryptionService.cs` and related startup code handle encrypted DB connection configuration

The app also includes:

- secure password hashing via PBKDF2-backed hashing logic
- login lockout support
- operational audit logging
- activity tracking on important actions
- role-based permission checks

## Default Login

The default seeded administrator account is:

- Username: `admin`
- Password: `admin123`

This is created through the app's auth seed flow and is intended for initial setup and local development testing.

## Functional Modules

### 1. Dashboard and overview
The dashboard provides operational summary metrics and quick views of key retail data.

### 2. Products and catalog
Product management includes pricing, units, categories, codes, batch information, and inventory tracking.

### 3. Customers and suppliers
Manage customer master records, credit exposure, supplier data, and party activity.

### 4. Sales and invoicing
Generate and manage invoices, sales transactions, payment receipts, and billing records.

### 5. Stock and inventory controls
Track goods movement, stock transfer, stock audit, batch expiry, and stock policy checks.

### 6. Purchases and expenses
Capture purchases, supplier bills, expense records, and financial posting support.

### 7. Accounting and ledger flows
The accounting layer supports journals, account groups, cash books, financial years, and ledger data views.

### 8. Reports and dashboard analytics
The application includes report surfaces for sales, ledger, stock valuation, profit, and operational summaries.

### 9. Security and compliance
This includes role checks, admin safeguards, audit records, and action logging to protect critical operations such as deletion, adjustment, or access to sensitive features.

## Known Notes

- This application is a Windows-only desktop project and is not designed as a browser-based SaaS platform.
- The repository includes generated local artifacts such as `bin/`, `obj/`, and database files that are not always intended for source control use.
- Some features documented in the project history are enterprise-oriented and may require additional external integration work to connect to live government or cloud APIs.
- Before deploying beyond local development, review backup, restore, and encryption settings for real operational usage.

## License

This project currently does not declare a formal license file in the repository root. If you plan to distribute or commercialize this project, confirm the applicable licensing before release.

## Contributing

Contributions are welcome in the form of bug fixes, feature enhancements, documentation improvements, and validation updates. When working on the project:

1. Keep changes aligned with the existing layered architecture.
2. Prefer service-layer logic over UI-specific implementation.
3. Validate WPF/XAML changes carefully because binding and resource changes can affect app behavior.
4. Preserve access-control and audit flows when changing sensitive business logic.

For local development, it is recommended to test the app after changes by building the project and running the WPF app in debug mode.
