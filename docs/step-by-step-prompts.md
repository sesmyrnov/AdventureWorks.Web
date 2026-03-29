
# AdventureWorksLT → Azure Cosmos DB NoSQL Migration: Step-by-Step Prompts

This document captures the exact sequential prompts used to migrate the AdventureWorksLT application from SQL Server/EF Core to Azure Cosmos DB NoSQL using GitHub Copilot as an AI coding agent.

---

## Prompt 1 — Migration Assessment Report

> **Prompt:** Generate an assessment report for migrating the AdventureWorksLT application from its current SQL Server/relational database backend to Azure Cosmos DB NoSQL. Include analysis of the current data model (see Models/ and AdventureWorksLT/AdventureworksLT.sql), access patterns (see Controllers/), dependencies, and migration risks.
Use access-patterns-tempalate.md and volumentrics-template.md as examples to document discovered access patterns and volumetrics projections based on sample data for 2 year retention.

Expected output(s): 
```
docs/cosmos-db-migration-assessment.md

docs/access-patterns.md

docs/volumentrics.md
```
---

## Prompt 2 — Schema & Access Patterns Conversion Plan

> **Prompt:** Based on the assessment report, create a detailed schema and access patterns conversion plan for Cosmos DB NoSQL. Design the document models, partition keys, container strategy, indexing policies, and query mappings.
-	Exclude ErrorLog, BuildVersion: Operational/metadata tables from schema/access patterns conversion plan ( we will not migrate them to Cosmos NoSQL).
-	Add SalesOrder Header/Details tables for Data migration to Cosmos NoSQL scope and add respective controller to scope of App Modernization plans ( re-analyze and update respective file). Evaluate based on datamodel accounting existing Customer domain/entity decisions for optimization. Order search will be always for a specific CustomerId 
-	Evaluate combining all product/categories/models related containers to gain some efficiencies


Expected output(s):
```
docs/schema_and_access_patterns_conversion_plan.md
```
---

## Prompt 3 — Bicep Infrastructure-as-Code

> **Prompt:** Based on schema_and_access_patterns_conversion_plan.md - generate Bicep infrastructure-as-code to provision the Cosmos DB account, database, and containers with the designed schema, partition keys, and indexing policies. 
Use EntraID auth (DefaultAzureCredential) – grant standard ControlPlane Operator and DataPlane Contributor R/W RBAC roles.


Expected output(s):
```
- `infra/main.bicep` — Cosmos DB account, database, containers, indexing policies, RBAC role assignments
- `infra/main.bicepparam` — Deployment parameters
```

**Include following variables in Bicep parameter file** (in `infra/main.bicepparam`):
```
param location = '<azure-region>'                  # e.g., 'eastus2'
param cosmosAccountName = '<cosmos-account-name>'   # e.g., 'myapp-cosmos-01'
param databaseName = '<database-name>'              # e.g., 'adventureworks'
param principalId = '<entra-principal-id>'          # e.g., '00000000-0000-0000-0000-000000000000'
param tags = {
  owner: '<owner-alias>'
}
```

use following parms for deployment script generation:
```
resource-group '<your-rg>'
```
---

## Prompt 4 — Deploy Infrastructure to Azure

> **Prompt:** Deploy the Bicep infrastructure to Azure to provision the Cosmos DB account, database, and containers.
- Create the resource group if it does not exist.
- Deploy the main.bicep template at the resource group scope with all required parameters.
- Verify that the Cosmos DB account, database, and containers are provisioned with the correct partition keys, indexing policies, and RBAC roles.
- Output the deployment and verification commands and results.


**Deployment command:**
```bash
az deployment group create \
  --resource-group <resource-group-name> \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

---

## Prompt 5 — Convert CSV sample data to JSON and load into target containers

> **Prompt:** Convert CSV sample data to JSON and load into target containers. Use CSV sample data files to generate target JSON Documents for each Entity following the schema defined in the schema_and_access_patterns_conversion_plan.md and load them into created Cosmos DB containers.Validate document counts match expected volumetrics.
- Save generated JSON in `/DataMigration/data` before loading to Cosmos DB and any code generated for data conversion in  `/DataMigration/tools`.

---

## Prompt 6 — Application Conversion Plan Document

> **Prompt:** Read all source code files (controllers, models, views, configuration, startup),  schema_and_access_patterns_conversion_plan.md and generate a comprehensive application conversion plan document detailing every code change needed to convert the application to Cosmos DB (switch from EF Core to native Cosmos DB .NET SDK), convert Cosmos DB client Auth to use Entra ID auth.
>The plan must explicitly address:
> - **Newtonsoft.Json dependency:** The Cosmos DB .NET SDK v3.x has a hard runtime
>   dependency on `Newtonsoft.Json >= 10.0.2`. Add it as an explicit PackageReference
>   (e.g., v13.0.3). Do NOT rely on `AzureCosmosDisableNewtonsoftJsonCheck` — that
>   only suppresses the build error but still crashes at runtime.
> - **CosmosClientBuilder namespace:** `CosmosClientBuilder` lives in
>   `Microsoft.Azure.Cosmos.Fluent`, not `Microsoft.Azure.Cosmos`. Include the
>   correct `using` directive in any service that builds the client.
> - **Nullable reference types:** When upgrading to net8.0+ with `<Nullable>enable</Nullable>`,
>   audit all existing non-model files (e.g., `ErrorViewModel.cs`) for properties
>   that must become nullable (`string?`) to avoid CS8618 warnings.


Expected output(s):
```
docs/application_conversion_plan.md
```
---

## Prompt 7 — Execute Application Conversion

> **Prompt:** Following the final schema_and_access_patterns_conversion_plan.md and application_conversion_plan.md — rewrite the application for Cosmos DB NoSQL, start application and run API validation tests.

>
> **Success Criteria’s:**
> - Goal 1 — Successful build with 0 errors
> - Goal 2 — Application starts successfully and returns local URL for web testing
> - Goal 3 — API validation tests passed successfully
>
> **API Validation Test Requirements:**
> - **Dynamic ID discovery:** Tests must extract entity IDs from listing pages
>   (e.g., parse hrefs from the Categories Index) rather than hardcoding
>   ID-to-name assumptions. Migrated data may sort/number differently.
> - **Route convention awareness:** Use MVC default routing
>   `{controller}/{action}/{id}`. For entities with composite keys (e.g.,
>   SalesOrders needing both orderId and customerId), the route `{id}` param
>   carries the primary key and additional keys go as query params
>   (`/SalesOrders/Details/{orderId}?customerId={cid}`).
> - **BaseUrl from launchSettings:** Read the configured port from
>   `Properties/launchSettings.json` rather than assuming a fixed port.



### Goal 1 — Build Result
- `dotnet build` → **Build succeeded with 0 errors**

### Goal 2 — Application Start
- `dotnet run --urls "https://localhost:<port>"` → **Application running and responding to requests**

### Goal 3 — Validation Tests (9/9 PASS)

| Test | Endpoint | Status | Verified |
|------|----------|--------|----------|
| ProductCategories Index | `/ProductCategories` | 200 PASS | Categories listed, parent names populated |
| ProductCategories Details | `/ProductCategories/Details/{id}` | 200 PASS | Point read, parent category name shown |
| Products Index | `/Products` | 200 PASS | Products listed with denormalized CategoryName |
| Products Details | `/Products/Details/{id}?categoryId={pk}` | 200 PASS | Point read using partition key |
| Customers Index | `/Customers` | 200 PASS | Customers listed from cross-partition query |
| Customers Details | `/Customers/Details/{id}` | 200 PASS | Customer point read with embedded addresses |
| SalesOrders Index | `/SalesOrders?customerId={id}` | 200 PASS | Orders for customer (single-partition query) |
| SalesOrder Details | `/SalesOrders/Details/{id}?customerId={id}` | 200 PASS | Order with embedded line items |
| Home/About | `/Home/About` | 200 PASS | Static page works |

---

## Technology Stack Summary

| Component | Value |
|-----------|-------|
| Framework | ASP.NET Core MVC |
| Source TFM | `netcoreapp2.1` (EF Core 2.1 / SQL Server) |
| Target TFM | `net9.0` |
| Cosmos SDK | `Microsoft.Azure.Cosmos` |
| Auth | `Azure.Identity` (`DefaultAzureCredential`) |
| Cosmos Account | `<cosmos-account-name>` |
| Endpoint | `https://<cosmos-account-name>.documents.azure.com:443/` |
| Database | `<database-name>` |
| Containers | `customer-orders` (PK `/customerId`), `product-catalog` (PK `/productCategoryId`) |
| Capacity Mode | Serverless |
| Infrastructure | Bicep (`infra/main.bicep` + `infra/main.bicepparam`) |
| Data Migration | Node.js (`@azure/cosmos` + `@azure/identity`) |
| RBAC Principal | `<entra-principal-id>` |
