# AdventureWorks SQL Server → Cosmos DB Migration — Step-by-Step Prompts

A reproducible, prompt-by-prompt guide for migrating the AdventureWorks ASP.NET Core MVC app from SQL Server / Entity Framework Core to Azure Cosmos DB for NoSQL using GitHub Copilot.

---

## Prerequisites

| Requirement | Details |
|---|---|
| Source repo | AdventureWorks.Web — ASP.NET Core 2.1 MVC + EF Core + SQL Server |
| Azure subscription | With permissions to create Cosmos DB resources |
| Azure CLI / `azd` | Authenticated (`az login` / `azd login`) |
| .NET 9.0 SDK | Installed locally |
| GitHub Copilot | Agent mode in VS Code |

---

## Phase 1 — Data Discovery & Migration Plan

### Step 1: Generate a Data Discovery Report

> **Prompt:**
> ```
> Analyze the existing codebase and schema/ folder. Produce a data discovery
> report covering: all SQL tables, column types, row counts (from CSV files),
> relationships (PKs/FKs), indexes, and access patterns used by the controllers
> and views.
> ```

**What Copilot does:**
- Reads all model files (`Models/*.cs`), the EF Core `DbContext`, controllers, views, and CSV files in `schema/`
- Identifies 11 SQL tables, their columns, relationships, and how the app accesses them
- Produces a discovery report documenting the current state

---

### Step 2: Generate the 8-Phase Migration Plan

> **Prompt:**
> ```
> Read the cosmos-nosql-copilot skill instructions. Using the data discovery
> report and the skill's methodology, generate a complete 8-phase migration
> plan in docs/migration_plan.md covering:
>   Phase 1 — Relational Inventory
>   Phase 2 — Schema Translation
>   Phase 3 — Aggregate / Document Design
>   Phase 4 — Access-Pattern Catalogue
>   Phase 5 — Partition-Key Selection
>   Phase 6 — Access-Pattern → SDK-Call Mapping
>   Phase 7 — Migration Strategy
>   Phase 8 — Validation Checklist
> Include concrete JSON document examples for every document type.
> ```

**What Copilot does:**
- Reads the `cosmosdb-best-practices` skill for methodology guidance
- Maps 11 SQL tables → 2 Cosmos containers (`products`, `customers`)
- Designs 5 document types: `product`, `productCategory`, `productModel`, `customer`, `salesOrder`
- Defines partition keys (`/id` for products, `/customerId` for customers)
- Documents denormalization strategy, embedding decisions, and ID schemes
- Outputs `docs/migration_plan.md` (~930 lines)

**Key design decisions captured:**
- Category ID offset scheme: parent categories keep IDs 1–4, subcategories get `subcategoryId + 100`
- Product IDs: `product-{ProductID}`, Category IDs: `category-{id}`, Model IDs: `model-{ProductModelID}`
- Customer/SalesOrder IDs: string of the integer ID
- Addresses embedded as arrays in customer documents
- Line items embedded as arrays in sales order documents
- Ship method, customer name, product name denormalized into orders

---

## Phase 2 — Infrastructure Provisioning

### Step 3: Create Bicep Infrastructure

> **Prompt:**
> ```
> Create Bicep infrastructure in infra/ to provision:
>   - Azure Cosmos DB for NoSQL account (Serverless capacity)
>   - Database: adventureworks
>   - Container: products (partition key /id)
>   - Container: customers (partition key /customerId)
>   - Entra ID RBAC: assign Cosmos DB Built-in Data Contributor role
>     to my user principal
> Use Azure Verified Modules where available.
> ```

**What Copilot does:**
- Creates `infra/modules/cosmos.bicep` — Cosmos account, database, and 2 containers
- Creates `infra/main.bicep` — orchestrates modules and role assignment
- Creates `infra/main.bicepparam` — parameterizes endpoint, location, principal ID
- Configures serverless capacity mode, camelCase serialization, automatic indexing

**Files created:**
```
infra/
  main.bicep
  main.bicepparam
  modules/
    cosmos.bicep
```

---

### Step 4: Deploy Infrastructure

> **Prompt:**
> ```
> Deploy the Bicep infrastructure to Azure.
> Resource group: ssm-cosmosdb-adventureworks01-rg
> Location: eastus2
> ```

**What Copilot does:**
- Runs `az deployment group create` targeting the resource group
- Provisions the Cosmos DB account (`ssm-cosmos-adventureworks01`), database, and containers
- Assigns RBAC role for Entra ID authentication

---

### Step 5: Smoke Test SDK Connectivity

> **Prompt:**
> ```
> Create a smoke test in infra/smoke-test/ that uses the Cosmos DB SDK
> with DefaultAzureCredential to connect, list containers, and upsert
> then read back a test document. Run it to verify RBAC auth works.
> ```

**What Copilot does:**
- Creates a small .NET console app in `infra/smoke-test/`
- Connects using `DefaultAzureCredential` (no connection strings)
- Verifies both containers exist, upserts a test doc, reads it back
- Confirms SDK + RBAC authentication works end-to-end

---

## Phase 3 — Application Code Migration

### Step 6: Rewrite Models for Cosmos DB

> **Prompt:**
> ```
> Following the migration plan document models, rewrite the application for
> Cosmos DB:
> 1. Update csproj to net9.0, remove EF Core, add Microsoft.Azure.Cosmos
>    and Azure.Identity packages
> 2. Delete obsolete model files that are no longer needed
> 3. Rewrite remaining models (Product, ProductCategory, ProductModel,
>    Customer) with string IDs, docType discriminator, and denormalized fields
> 4. Create new SalesOrder model with embedded line items and address snapshots
> 5. Create CosmosDbService with full CRUD operations
> 6. Rewrite Program.cs for .NET 9.0 minimal hosting with Cosmos client
>    registration using DefaultAzureCredential and camelCase serialization
> 7. Rewrite all 4 controllers to use CosmosDbService
> 8. Update all views for the new model shapes
> Build and confirm 0 errors.
> ```

**What Copilot does:**

*Project file:*
- Updates `AdventureWorks.Web.csproj` to `net9.0`
- Removes EF Core packages, adds `Microsoft.Azure.Cosmos`, `Azure.Identity`

*Models deleted (9 files):*
- `Address.cs`, `BuildVersion.cs`, `CustomerAddress.cs`, `ErrorLog.cs`
- `ProductDescription.cs`, `ProductModelProductDescription.cs`
- `SalesOrderDetail.cs`, `SalesOrderHeader.cs`, `sampledbContext.cs`

*Models rewritten (4 files):*
- `Product.cs` — string `Id`, `DocType`, denormalized `CategoryName`, `ParentCategoryName`, `ModelName`
- `ProductCategory.cs` — string `Id`, `DocType`, nullable `ParentProductCategoryId`, `ParentCategoryName`
- `ProductModel.cs` — string `Id`, `DocType`, embedded `Descriptions` list
- `Customer.cs` — string `Id`/`CustomerId`, `DocType`, embedded `Addresses` list

*New model (1 file):*
- `SalesOrder.cs` — embedded `LineItems` list, `BillToAddress`/`ShipToAddress` objects, denormalized `CustomerName`, `ShipMethod`, `ProductName`

*New service:*
- `Services/CosmosDbService.cs` — generic CRUD using Cosmos SDK, query by docType, partition key routing

*Rewritten:*
- `Program.cs` — .NET 9.0 minimal hosting, `CosmosClient` DI registration with `DefaultAzureCredential`
- All 4 controllers (`HomeController`, `ProductCategoriesController`, `ProductsController`, `CustomersController`)
- All views updated for new property names and string IDs

**Build result:** 0 errors

---

## Phase 4 — Data Migration

### Step 7: Create Data Migration Console App

> **Prompt:**
> ```
> Create a data migration console app in tools/DataMigration/ based on the
> migration plan. Requirements:
> 1. Read CSV files from schema/ folder handling BOTH tab-delimited AND
>    pipe-delimited (+| field separator, &| row terminator) formats
> 2. Transform data into target document models — joining across CSVs to
>    denormalize (e.g., Person + EmailAddress + Phone + Address → Customer;
>    SalesOrderHeader + Details + ShipMethod + Address → SalesOrder;
>    ProductCategory + ProductSubcategory with ID offset → categories)
> 3. Use native Cosmos DB SDK with AllowBulkExecution = true
> 4. Set docType discriminator on every document
> 5. Use CosmosPropertyNamingPolicy.CamelCase
> 6. Batch sizes: categories (all at once), products (100/batch),
>    customers (100/batch), orders (50/batch)
> 7. Report progress and error counts
>
> Run the migration and verify document counts match expected:
>   products container: 41 categories + 504 products = 545
>   customers container: 19,119 customers + 31,465 orders = 50,584
> ```

**What Copilot does:**

*CSV format analysis:*
- Reads first lines of all 18 CSV files to determine delimiter format
- Identifies tab-delimited files: ProductCategory, ProductSubcategory, Product, ProductDescription, PMPDC, Customer, Address, SalesOrderHeader, SalesOrderDetail, ShipMethod, StateProvince, AddressType
- Identifies pipe-delimited files (`+|`/`&|`): ProductModel, Person, EmailAddress, Password, PersonPhone, BusinessEntityAddress

*Files created:*
```
tools/DataMigration/
  DataMigration.csproj    — net9.0, Azure.Identity, Microsoft.Azure.Cosmos
  Program.cs              — ~850 lines, full ETL pipeline
```

*Migration pipeline (Program.cs):*
1. **CSV parsing** — two parsers: `ReadTabFile()` for tab-delimited, `ReadPipeFile()` for `+|`/`&|` pipe-delimited
2. **Lookup dictionaries** — builds 18+ in-memory dictionaries for joins and denormalization
3. **Transform & load** — 5 document types:
   - `productCategory` (41 docs) — parent categories + subcategories with ID+100 offset
   - `productModel` (128 docs) — with embedded descriptions array
   - `product` (504 docs) — with denormalized category/model names
   - `customer` (19,119 docs) — joined from Customer + Person + Email + Phone + Password + Address tables, filtering out store-only customers
   - `salesOrder` (31,465 docs) — joined from Header + Detail + ShipMethod + Address, with embedded line items and address snapshots
4. **Bulk upsert** — `AllowBulkExecution = true`, concurrent `Task.WhenAll`, 429 retry handling

**Migration result:**
```
Products container:  673 documents (41 categories + 128 models + 504 products)
Customers container: 50,584 documents (19,119 customers + 31,465 orders)
Grand total:         51,257 documents
Errors:              0
Elapsed:             00:03:41
```

---

## Phase 5 — End-to-End Verification

### Step 8: Start App and Test All Endpoints

> **Prompt:**
> ```
> 1. Start the web app with dotnet run
> 2. Test all controller endpoints:
>    - GET / (ProductCategories/Index) — verify row count matches categories
>    - GET /Products — verify row count matches products
>    - GET /Products/Details/680 — verify "HL Road Frame" appears
>    - GET /Customers/Details/11000 — verify "Jon Yang" appears
> 3. Report results
> ```

**What Copilot does:**
- Starts the app with `dotnet run` (Kestrel on `http://localhost:5000` / `https://localhost:5001`)
- Issues HTTP requests to each endpoint and validates responses

**Test results:**

| Endpoint | Status | Validation |
|---|---|---|
| `GET /` (ProductCategories/Index) | 200 | **41 table rows** — matches Cosmos |
| `GET /Products` | 200 | **504 table rows** — matches Cosmos |
| `GET /Products/Details/product-680` | 200 | **"HL Road Frame" found** |
| `GET /Customers/Details/11000` | 200 | **"Jon Yang" found** |

All endpoints returned HTTP 200 with expected data. Migration complete.

---

## Summary — Files Changed

### New Files Created
| File | Purpose |
|---|---|
| `docs/migration_plan.md` | 8-phase migration plan with document model designs |
| `infra/main.bicep` | Infrastructure orchestration |
| `infra/main.bicepparam` | Bicep parameters |
| `infra/modules/cosmos.bicep` | Cosmos DB account, database, containers |
| `infra/smoke-test/` | SDK connectivity smoke test |
| `Models/SalesOrder.cs` | New sales order document model |
| `Services/CosmosDbService.cs` | Cosmos DB data access service |
| `tools/DataMigration/DataMigration.csproj` | Migration console app project |
| `tools/DataMigration/Program.cs` | CSV → Cosmos ETL pipeline |

### Modified Files
| File | Change |
|---|---|
| `AdventureWorks.Web.csproj` | net9.0, removed EF Core, added Cosmos SDK + Azure.Identity |
| `Program.cs` | Rewritten for .NET 9.0 minimal hosting with Cosmos client |
| `Models/Product.cs` | Rewritten with string ID, docType, denormalized fields |
| `Models/ProductCategory.cs` | Rewritten with string ID, docType, parent info |
| `Models/ProductModel.cs` | Rewritten with string ID, docType, embedded descriptions |
| `Models/Customer.cs` | Rewritten with string ID, docType, embedded addresses |
| `Controllers/*.cs` | All 4 controllers rewritten for CosmosDbService |
| `Views/**/*.cshtml` | Updated for new model property names and string IDs |

### Deleted Files
| File | Reason |
|---|---|
| `Models/Address.cs` | Embedded in Customer document |
| `Models/BuildVersion.cs` | Not needed in Cosmos |
| `Models/CustomerAddress.cs` | Embedded in Customer document |
| `Models/ErrorLog.cs` | Not needed in Cosmos |
| `Models/ProductDescription.cs` | Embedded in ProductModel document |
| `Models/ProductModelProductDescription.cs` | Junction table, eliminated |
| `Models/SalesOrderDetail.cs` | Embedded in SalesOrder document |
| `Models/SalesOrderHeader.cs` | Replaced by SalesOrder.cs |
| `Models/sampledbContext.cs` | EF Core DbContext, eliminated |

---

## Architecture Before & After

```
BEFORE                                    AFTER
──────                                    ─────
ASP.NET Core 2.1                          ASP.NET Core 9.0
EF Core + SQL Server                      Cosmos DB SDK + NoSQL
Connection string auth                    Entra ID RBAC (DefaultAzureCredential)
11 normalized SQL tables                  2 containers, 5 document types
Multiple JOINs per query                  Single point-read or single-partition query
```
