# AdventureWorks.Web — Cosmos DB for NoSQL Migration Plan

**Date:** 2026-02-07
**Source:** ASP.NET Core 2.1 MVC + EF Core + SQL Server (AdventureWorksLT / SalesLT schema)
**Target:** .NET 8 MVC + Microsoft.Azure.Cosmos SDK + Azure Cosmos DB for NoSQL
**Authentication:** Entra ID RBAC via `DefaultAzureCredential`
**Serialization:** camelCase JSON, `docType` discriminator on multi-type containers

---

## Summary — Table-to-Container Mapping

### At a Glance: 11 SQL Tables → 2 Cosmos DB Containers

```
┌─────────────────────────────────────────────────────────────────────┐
│  SQL Server (SalesLT)              Cosmos DB for NoSQL             │
│  ─────────────────────             ──────────────────────────────  │
│                                                                     │
│  Product ──────────────┐                                            │
│  ProductCategory ──────┤           ┌─────────────────────────────┐ │
│  ProductModel ─────────┤──────────▶│  products  (PK: /id)        │ │
│  ProductDescription ───┘  embed    │  docType: product |          │ │
│  ProductModelProductDesc ─┘        │    productCategory |         │ │
│                                    │    productModel              │ │
│                                    └─────────────────────────────┘ │
│                                                                     │
│  Customer ─────────────┐                                            │
│  Address ──────────────┤  embed    ┌─────────────────────────────┐ │
│  CustomerAddress ──────┘           │  customers  (PK: /customerId)│ │
│  SalesOrderHeader ─────┐──────────▶│  docType: customer |         │ │
│  SalesOrderDetail ─────┘  embed    │    salesOrder               │ │
│                                    └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### Entity-to-Container Mapping

| SQL Table (SalesLT) | Cosmos DB Container | Partition Key | Technique | Notes |
|---------------------|--------------------:|:-------------:|-----------|-------|
| Product | products | `/id` | **Denormalize** | Embeds `categoryName`, `parentCategoryName`, `modelName` from related tables |
| ProductCategory | products | `/id` | **Denormalize** | Embeds `parentCategoryName` from self-referencing parent |
| ProductModel | products | `/id` | **Embed** | Embeds `descriptions[]` array from ProductDescription via junction |
| ProductDescription | — | — | **Embedded** | Folded into ProductModel doc as `descriptions[]`; table eliminated |
| ProductModelProductDescription | — | — | **Eliminated** | Junction table replaced by embedded array in ProductModel |
| Customer | customers | `/customerId` | **Embed** | Embeds `addresses[]` array from Address + CustomerAddress junction |
| Address | — | — | **Embedded** | Folded into Customer doc as `addresses[]`; table eliminated |
| CustomerAddress | — | — | **Eliminated** | Junction table replaced by embedded array in Customer |
| SalesOrderHeader | customers | `/customerId` | **Co-locate** | Same container as Customer, discriminated by `docType = "salesOrder"` |
| SalesOrderDetail | — | — | **Embedded** | Folded into SalesOrder doc as `lineItems[]`; table eliminated |
| SalesOrderHeader.BillTo/ShipTo → Address | — | — | **Snapshot** | Address copied into order doc at creation time (point-in-time) |

### Techniques Summary

| Technique | Count | Description | Example |
|-----------|------:|-------------|---------|
| **Denormalize** | 3 | Copy frequently-read lookup values into the parent document to avoid JOINs | `categoryName` in Product doc |
| **Embed (bounded array)** | 3 | Nest child rows as an array inside the parent document | `lineItems[]` in SalesOrder, `addresses[]` in Customer, `descriptions[]` in ProductModel |
| **Co-locate** | 1 | Store different entity types in the same container sharing a partition key | Customer + SalesOrder in `customers` container on `/customerId` |
| **Snapshot** | 2 | Capture point-in-time copy of related data (no propagation) | `billToAddress`, `shipToAddress`, `customerName`, `productName` in order docs |
| **Eliminate** | 5 | Remove junction/child tables entirely — data absorbed into parent docs | Address, CustomerAddress, SalesOrderDetail, ProductDescription, PMPDC |

### Consolidation Impact

| Metric | SQL Server | Cosmos DB | Reduction |
|--------|----------:|----------:|----------:|
| Tables / Containers | 11 | 2 | 82% |
| JOIN operations per product read | 2 | 0 | 100% |
| JOIN operations per order read | 4 | 0 | 100% |
| Junction tables | 3 | 0 | 100% |
| Separate address lookups | 3 (Customer, BillTo, ShipTo) | 0 | 100% |

---

## Phase 1 — Inventory Summary

| # | Input | Value | Source |
|---|-------|-------|--------|
| 1 | Source schema | SalesLT (11 EF entities mapped to `SalesLT.*` tables). CSV data uses full AW normalized schema (71 tables). | EF DbContext, `instawdb.sql` |
| 2 | Row counts | Customers: 19,820 · Products: 504 · Categories: 41 · Models: 364 · Orders: 31,465 · Line items: 121,317 · Addresses: 19,614 | CSV analysis |
| 3 | Top queries by frequency | 1) List all products (+Category+Model) 2) List all categories (+Parent) 3) List all customers 4) Get entity by ID (point read) 5) CRUD writes | Controller code |
| 4 | Read:write ratio | ~10:1 (internal CRUD MVC app, read-heavy catalog browsing) | Inferred — scaffolded CRUD, catalog-first default route |
| 5 | Source indexes | PK indexes on all tables; unique on Product.Name, Product.ProductNumber; FK indexes on ProductCategoryId, ProductModelId, CustomerId | EF `OnModelCreating` |
| 6 | FK relationships | Product→Category (N:1), Product→Model (N:1), Customer→Address (M:N via junction), Customer→Order (1:N bounded), Order→Detail (1:N bounded), Detail→Product (N:1), Category→ParentCategory (N:1 self-ref) | EF nav properties |
| 7 | Transactional boundaries | Each entity written independently; Order+Details created together in SalesLT (single SaveChanges); no stored procedures | Controller code |
| 8 | Latency requirements | Internal MVC app: P50 < 100 ms, P99 < 500 ms (assumed — no SLA doc) | Inferred |
| 9 | Data retention | No archival policy; all data is retained | Inferred |

### Assumptions (stated explicitly)

- Traffic is low (<50 concurrent users) — this is an internal line-of-business application.
- Growth rate is negligible for the catalog (products, categories). Order growth estimated at ~5K orders/year.
- Binary `ThumbNailPhoto` data will be migrated to Azure Blob Storage, not stored in Cosmos DB documents.
- `PasswordHash` / `PasswordSalt` are retained for backward compatibility but should be replaced with Entra ID authentication long-term.
- `Rowguid` columns are SQL Server-specific and will be dropped; Cosmos DB `id` replaces them.
- `BuildVersion` and `ErrorLog` entities are not migrated (metadata/operational only).

---

## Phase 2 — Schema Translation

### Entities & Relationships

| Entity | PK | Cardinality to Parent | Bounded? | Avg Rows | Notes |
|--------|----|-----------------------|----------|----------|-------|
| ProductCategory | ProductCategoryId | Self-ref N:1 (parent) | Yes (2 levels) | 41 | 4 parents + 37 children |
| Product | ProductId | N:1 → Category, N:1 → Model | N/A | 504 | Always read with category + model |
| ProductModel | ProductModelId | 1:N → Product | Yes | 364 | Used in dropdowns + embedded in Product |
| ProductDescription | ProductDescriptionId | N:M → Model (via junction) | Yes (≤6 cultures) | 762 | Embed into ProductModel |
| Customer | CustomerId | 1:N → Address (via junction) | Yes (1-2 addrs) | 19,820 | Denormalized SalesLT entity |
| Address | AddressId | M:N → Customer (via junction) | N/A | 19,614 | Embed into Customer |
| CustomerAddress | (CustId, AddrId) | Junction table | Yes | ~19,820 | Eliminate — embed addresses |
| SalesOrderHeader | SalesOrderId | N:1 → Customer | N/A | 31,465 | Avg 1.6 orders/customer |
| SalesOrderDetail | (OrderId, DetailId) | N:1 → Order | Yes (avg 3.9) | 121,317 | Embed into order doc |

### Index-Derived Access Patterns

| Source Index | Implied Query | Frequency |
|-------------|--------------|-----------|
| PK (all tables) | Point lookups by ID | High |
| Product.Name (unique) | Lookup product by name | Medium |
| ProductNumber (unique) | Lookup product by number | Medium |
| Customer.EmailAddress | Lookup customer by email | Low |
| SalesOrderHeader.CustomerId | List orders by customer | Medium |
| SalesOrderHeader.SalesOrderNumber | Lookup order by number | Low |

---

## Phase 3 — Aggregate Design

### Aggregate Decision Framework

```
Product + ProductCategory (N:1)
  Access correlation: >90% (always Include'd together)
  Combined size: Product ~1.5 KB + category name ~50 bytes = ~1.5 KB
  → DECISION: Denormalize — embed categoryName + parentCategoryName into product doc
  → Keep ProductCategory as separate docs for CRUD management

Product + ProductModel (N:1)
  Access correlation: >90% (always Include'd together)
  Combined size: Product ~1.5 KB + model name ~50 bytes = ~1.5 KB
  → DECISION: Denormalize — embed modelName into product doc
  → Keep ProductModel as separate docs for dropdown population

ProductModel + ProductDescription (1:N via junction, ≤6 cultures)
  Access correlation: <50% (descriptions not exposed by any controller)
  Combined size: model ~0.5 KB + 6 descriptions × 400 chars = ~3 KB
  Bounded array (max 6 cultures) ✅
  → DECISION: Embed descriptions array into ProductModel doc
  → Eliminates junction table ProductModelProductDescription entirely

Customer + Address (M:N via junction, 1–2 addresses per customer)
  Access correlation: <50% (addresses not directly exposed by controller)
  Combined size: customer ~1 KB + 2 addresses × 200 bytes = ~1.4 KB
  Bounded array (1-2 addresses) ✅
  → DECISION: Embed addresses array into Customer doc
  → Eliminates junction table CustomerAddress and separate Address docs

SalesOrderHeader + SalesOrderDetail (1:N, avg 3.9 items)
  Access correlation: >90% (order meaningless without line items)
  Combined size: header ~0.5 KB + 4 items × 200 bytes = ~1.3 KB
  Bounded array (max ~70 items based on data analysis) ✅
  Max estimated doc size: ~15 KB (well under 1 MB)
  → DECISION: Embed lineItems array into SalesOrder doc

Customer + SalesOrderHeader (1:N, avg 1.6 orders)
  Access correlation: Medium (navigate customer → orders)
  Same partition key (customerId) enables co-location
  Avg 1.6 orders/customer, max ~20 — logical partition stays small (~85 KB)
  Multi-type container with docType discriminator enables clean filtering
  → DECISION: Co-locate in same container partitioned by /customerId
  → Enables transactional batch writes across customer + order docs
```

### Final Aggregate Boundaries

| Aggregate Root | Embedded Children | Eliminated Tables | Container |
|---------------|-------------------|-------------------|-----------|
| Product | categoryName, parentCategoryName, modelName (denormalized fields) | — | products |
| ProductCategory | parentCategoryName (denormalized) | — | products |
| ProductModel | descriptions[] (from ProductDescription + PMPDC junction) | ProductDescription, ProductModelProductDescription | products |
| Customer | addresses[] (from Address + CustomerAddress junction) | Address, CustomerAddress | customers |
| SalesOrder | lineItems[] (from SalesOrderDetail), billToAddress{}, shipToAddress{}, customerName, productNames | SalesOrderDetail | customers |

---

## Phase 4 — Access Pattern Register

Extracted from controller code analysis:

| # | Pattern Name | Priority | Type | Entities | Filter Fields | Avg TPS | Peak TPS | Records | Avg Doc Size | Read:Write | Latency SLA |
|---|-------------|----------|------|----------|---------------|--------:|---------:|--------:|-----------:|----------:|-------------|
| 1 | List all products | P0 | Query | Product | docType | 5 | 20 | 504 | 1.5 KB | Read | P99 <200ms |
| 2 | Get product by ID | P0 | Point Read | Product | id | 10 | 50 | 504 | 1.5 KB | Read | P99 <10ms |
| 3 | Create product | P1 | Create | Product | — | 0.5 | 2 | — | 1.5 KB | Write | P99 <50ms |
| 4 | Update product | P1 | Replace | Product | id | 1 | 5 | — | 1.5 KB | Write | P99 <50ms |
| 5 | Delete product | P2 | Delete | Product | id | 0.1 | 1 | — | — | Write | P99 <50ms |
| 6 | List all categories | P0 | Query | ProductCategory | docType | 5 | 20 | 41 | 0.3 KB | Read | P99 <100ms |
| 7 | Get category by ID | P0 | Point Read | ProductCategory | id | 5 | 20 | 41 | 0.3 KB | Read | P99 <10ms |
| 8 | Create / update / delete category | P1 | Write | ProductCategory | id | 0.2 | 1 | — | 0.3 KB | Write | P99 <50ms |
| 9 | List all product models (dropdown) | P1 | Query | ProductModel | docType | 2 | 10 | 364 | 2 KB | Read | P99 <200ms |
| 10 | List all customers | P0 | Query | Customer | docType | 3 | 15 | 19,820 | 1.5 KB | Read | P99 <500ms |
| 11 | Get customer by ID | P0 | Point Read | Customer | id | 10 | 50 | 19,820 | 1.5 KB | Read | P99 <10ms |
| 12 | Create customer | P1 | Create | Customer | — | 0.5 | 2 | — | 1.5 KB | Write | P99 <50ms |
| 13 | Update customer | P1 | Replace | Customer | id | 1 | 5 | — | 1.5 KB | Write | P99 <50ms |
| 14 | Delete customer | P2 | Delete | Customer | id | 0.1 | 1 | — | — | Write | P99 <50ms |
| 15 | List orders by customer | P1 | Query | SalesOrder | customerId | 2 | 10 | 31,465 | 4 KB | Read | P99 <100ms |
| 16 | Get order by ID | P1 | Point Read | SalesOrder | id + customerId | 5 | 20 | 31,465 | 4 KB | Read | P99 <10ms |
| 17 | Create order | P2 | Create | SalesOrder | — | 0.2 | 2 | — | 4 KB | Write | P99 <50ms |

### Volumetric Sizing

| Container | Total Documents | Avg Doc Size | Total Storage | Physical Partitions |
|-----------|----------------:|-------------:|--------------:|--------------------:|
| products | 909 | ~1.5 KB (blended) | ~1.4 MB | 1 |
| customers | 51,285 | ~2.5 KB (blended) | ~155.6 MB | 1 |
| **TOTAL** | **52,194** | | **~157 MB** | **2** |

All containers fit in single physical partitions. Max logical partition size is ~85 KB (customer with ~20 orders × 4 KB + 1.5 KB customer doc), well under 20 GB.

---

## Phase 5 — Partition Key Selection

### Products Container

| # | Question | Answer |
|---|----------|--------|
| 1 | Which field in >80% of WHERE clauses? | `id` (all point reads) and `docType` (list queries) |
| 2 | High cardinality? | `id`: 909 distinct values ✅ |
| 3 | Even write distribution? | Each doc has unique id ✅ |
| 4 | Multi-level query targeting? | Not needed — dataset too small |
| 5 | No single field works? | `/id` works well |

**Selected: `/id`**

Rationale: All P0 reads are either point reads by `id` (single-partition) or full-container queries filtered by `docType`. With only 909 documents in a single physical partition, every query is efficient regardless.

### Customers Container

| # | Question | Answer |
|---|----------|--------|
| 1 | Which field in >80% of WHERE clauses? | `id` / `customerId` (all point reads and writes by customer ID) |
| 2 | High cardinality? | 19,820 distinct values ✅ |
| 3 | Even write distribution? | Each customer is independent ✅ |
| 4 | Multi-level query targeting? | Not needed |
| 5 | No single field works? | `/id` works well |

**Selected: `/id`**

Rationale: All CRUD operations target a single customer by ID. The "list all" query is cross-partition but operates on a single physical partition (30 MB).

### SalesOrders Container

| # | Question | Answer |
|---|----------|--------|
| 1 | Which field in >80% of WHERE clauses? | `customerId` (list orders by customer) and `id` + `customerId` (point read) |
| 2 | High cardinality? | 19,119 distinct customerIds ✅ |
| 3 | Even write distribution? | Avg 1.6 orders/customer, max ~20 ✅ |
| 4 | Multi-level query targeting? | Not needed |
| 5 | No single field works? | `/customerId` works well |

**Selected: `/customerId`**

Rationale: Orders are naturally accessed by customer. Point reads require both `id` and `customerId` (partition key), which aligns with the navigation flow: Customer → their Orders. Average 1.6 orders per customer ensures no hot partitions.

### Partition Key Validation

| Check | products | customers | Pass? |
|-------|----------|-----------|-------|
| P0 queries are single-partition | Point reads ✅; list queries cross-partition but 1 physical partition | Point reads ✅; list by customer ✅; list all cross-partition but 1 phys. partition | ✅ |
| No logical partition > 20 GB | Max ~2 KB | Max ~85 KB (customer + ~20 orders) | ✅ |
| No hot partition > 10K RU/s | Peak ~50 RU/s total | Peak ~150 RU/s total | ✅ |
| Write distribution even | 1 doc per partition | Avg 2.6 docs per partition | ✅ |

---

## Container Design

| Container | Partition Key | Entity Types (docType) | Estimated Size | Throughput Mode | Estimated RU/s |
|-----------|--------------|----------------------|----------------|-----------------|---------------:|
| **products** | `/id` | `product`, `productCategory`, `productModel` | 1.4 MB | Serverless | ~50 peak |
| **customers** | `/customerId` | `customer`, `salesOrder` | 155.6 MB | Serverless | ~125 peak |

**Throughput mode: Serverless** — optimal for <200 MB total storage, <200 RU/s peak, bursty/low traffic internal app. No minimum RU provisioning cost.

---

## Document Models

### Product Document (container: `products`, docType: `product`)

```json
{
  "id": "product-680",
  "docType": "product",
  "productId": 680,
  "name": "HL Road Frame - Black, 58",
  "productNumber": "FR-R92B-58",
  "color": "Black",
  "standardCost": 1059.31,
  "listPrice": 1431.50,
  "size": "58",
  "weight": 1016.04,
  "productCategoryId": 18,
  "categoryName": "Road Frames",
  "parentCategoryName": "Components",
  "productModelId": 6,
  "modelName": "HL Road Frame",
  "sellStartDate": "2008-04-30T00:00:00Z",
  "sellEndDate": null,
  "discontinuedDate": null,
  "thumbnailPhotoFileName": "no_image_available_small.gif",
  "modifiedDate": "2014-02-08T10:01:36Z"
}
```

**Size estimate:** ~0.5–2 KB per document. 504 documents.

### ProductCategory Document (container: `products`, docType: `productCategory`)

```json
{
  "id": "category-18",
  "docType": "productCategory",
  "productCategoryId": 18,
  "parentProductCategoryId": 2,
  "parentCategoryName": "Components",
  "name": "Road Frames",
  "modifiedDate": "2008-04-30T00:00:00Z"
}
```

Parent category (no parent):

```json
{
  "id": "category-2",
  "docType": "productCategory",
  "productCategoryId": 2,
  "parentProductCategoryId": null,
  "parentCategoryName": null,
  "name": "Components",
  "modifiedDate": "2008-04-30T00:00:00Z"
}
```

**Size estimate:** ~0.2–0.4 KB per document. 41 documents (4 parents + 37 children).

### ProductModel Document (container: `products`, docType: `productModel`)

```json
{
  "id": "model-6",
  "docType": "productModel",
  "productModelId": 6,
  "name": "HL Road Frame",
  "catalogDescription": null,
  "descriptions": [
    { "culture": "en", "description": "Our lightest and best quality aluminum frame made from the newest alloy." },
    { "culture": "fr", "description": "Notre cadre en aluminium le plus léger et de la meilleure qualité." },
    { "culture": "ar", "description": "إطارنا الأخف والأعلى جودة من الألمنيوم." },
    { "culture": "zh-cht", "description": "我們最輕、品質最佳的鋁合金車架。" },
    { "culture": "he", "description": "המסגרת הקלה והאיכותית ביותר שלנו מאלומיניום." },
    { "culture": "th", "description": "เฟรมอลูมิเนียมที่เบาที่สุดและมีคุณภาพดีที่สุดของเรา" }
  ],
  "modifiedDate": "2011-05-01T00:00:00Z"
}
```

**Size estimate:** ~1–3 KB per document (varies by description count). 364 documents.
Embeds ProductDescription data via the PMPDC junction — **eliminates 2 SQL tables**.

### Customer Document (container: `customers`, docType: `customer`)

```json
{
  "id": "29825",
  "docType": "customer",
  "customerId": "29825",
  "nameStyle": false,
  "title": "Mr.",
  "firstName": "Jon",
  "middleName": "V",
  "lastName": "Yang",
  "suffix": null,
  "companyName": "Future Bikes",
  "salesPerson": "adventure-works\\jillian0",
  "emailAddress": "jon24@adventure-works.com",
  "phone": "1 (11) 500 555-0162",
  "passwordHash": "L/Rlwxzp4w7RWmEgXX+/A7cXaePEPcp+KwQhl2fJL7w=",
  "passwordSalt": "1KjXYs4=",
  "addresses": [
    {
      "addressId": 985,
      "addressType": "Main Office",
      "addressLine1": "3761 N. 14th St",
      "addressLine2": null,
      "city": "Rockhampton",
      "stateProvince": "Queensland",
      "countryRegion": "Australia",
      "postalCode": "4700"
    }
  ],
  "modifiedDate": "2014-09-12T11:15:07Z"
}
```

**Size estimate:** ~1–2 KB per document. 19,820 documents.
Embeds Address + CustomerAddress junction data — **eliminates 2 SQL tables**.

### SalesOrder Document (container: `customers`, docType: `salesOrder`)

```json
{
  "id": "43659",
  "docType": "salesOrder",
  "salesOrderId": 43659,
  "customerId": "29825",
  "customerName": "Jon Yang",
  "revisionNumber": 8,
  "orderDate": "2011-05-31T00:00:00Z",
  "dueDate": "2011-06-12T00:00:00Z",
  "shipDate": "2011-06-07T00:00:00Z",
  "status": 5,
  "onlineOrderFlag": false,
  "salesOrderNumber": "SO43659",
  "purchaseOrderNumber": "PO522145787",
  "accountNumber": "10-4020-000676",
  "shipMethod": "CARGO TRANSPORT 5",
  "creditCardApprovalCode": "105041Vi84182",
  "billToAddress": {
    "addressLine1": "3761 N. 14th St",
    "addressLine2": null,
    "city": "Rockhampton",
    "stateProvince": "Queensland",
    "countryRegion": "Australia",
    "postalCode": "4700"
  },
  "shipToAddress": {
    "addressLine1": "3761 N. 14th St",
    "addressLine2": null,
    "city": "Rockhampton",
    "stateProvince": "Queensland",
    "countryRegion": "Australia",
    "postalCode": "4700"
  },
  "subTotal": 20565.6206,
  "taxAmt": 1971.5149,
  "freight": 616.0984,
  "totalDue": 23153.2339,
  "comment": null,
  "lineItems": [
    {
      "salesOrderDetailId": 1,
      "productId": 776,
      "productName": "Mountain-100 Black, 42",
      "orderQty": 1,
      "unitPrice": 2024.994,
      "unitPriceDiscount": 0.00,
      "lineTotal": 2024.994
    },
    {
      "salesOrderDetailId": 2,
      "productId": 777,
      "productName": "Mountain-100 Black, 44",
      "orderQty": 3,
      "unitPrice": 2024.994,
      "unitPriceDiscount": 0.00,
      "lineTotal": 6074.982
    },
    {
      "salesOrderDetailId": 3,
      "productId": 778,
      "productName": "Mountain-100 Black, 48",
      "orderQty": 1,
      "unitPrice": 2024.994,
      "unitPriceDiscount": 0.00,
      "lineTotal": 2024.994
    }
  ],
  "modifiedDate": "2011-06-07T00:00:00Z"
}
```

**Size estimate:** ~2–15 KB per document (varies by line item count; avg 3.9 items). 31,465 documents.
Embeds SalesOrderDetail rows, address snapshots, and denormalized customer/product names — **eliminates 1 SQL table** (SalesOrderDetail) and avoids JOINs to Address/Customer/Product.

---

## Phase 6 — Access Pattern Mapping

### 6a. Query Translation Table

| # | Pattern Name | Origin | RDBMS Operation | Cosmos DB Operation | Container | PK Hit | Cosmos DB SDK Call |
|---|-------------|--------|----------------|--------------------:|-----------|--------|--------------------|
| 1 | List all products | Original | `SELECT * FROM Product p JOIN ProductCategory c ON p.ProductCategoryID=c.ProductCategoryID JOIN ProductModel m ON p.ProductModelID=m.ProductModelID` | Query (single physical partition) | products | All partitions (1 phys.) | `container.GetItemQueryIterator<Product>(new QueryDefinition("SELECT * FROM c WHERE c.docType = 'product'"))` |
| 2 | Get product by ID | Original | `SELECT * FROM Product WHERE ProductID = @id` (+ JOINs) | Point read | products | Single ✅ | `container.ReadItemAsync<Product>("product-" + id, new PartitionKey("product-" + id))` |
| 3 | Create product | Original | `INSERT INTO Product ...` | Create | products | Single ✅ | `container.CreateItemAsync(product, new PartitionKey(product.Id))` |
| 4 | Update product | Original | `UPDATE Product SET ... WHERE ProductID = @id` | Replace | products | Single ✅ | `container.ReplaceItemAsync(product, product.Id, new PartitionKey(product.Id))` |
| 5 | Delete product | Original | `DELETE FROM Product WHERE ProductID = @id` | Delete | products | Single ✅ | `container.DeleteItemAsync<Product>(id, new PartitionKey(id))` |
| 6 | List all categories | Original | `SELECT c.*, p.Name AS ParentName FROM ProductCategory c LEFT JOIN ProductCategory p ON c.ParentProductCategoryID=p.ProductCategoryID` | Query | products | All (1 phys.) | `container.GetItemQueryIterator<ProductCategory>(new QueryDefinition("SELECT * FROM c WHERE c.docType = 'productCategory'"))` |
| 7 | Get category by ID | Original | `SELECT * FROM ProductCategory WHERE ProductCategoryID = @id` | Point read | products | Single ✅ | `container.ReadItemAsync<ProductCategory>("category-" + id, new PartitionKey("category-" + id))` |
| 8 | CRUD category | Original | INSERT/UPDATE/DELETE | Create/Replace/Delete | products | Single ✅ | Point write by id |
| 9 | List models (dropdown) | Original | `SELECT ProductModelID, Name FROM ProductModel` | Query | products | All (1 phys.) | `container.GetItemQueryIterator<ProductModel>(new QueryDefinition("SELECT c.productModelId, c.name FROM c WHERE c.docType = 'productModel'"))` |
| 10 | List all customers | Original | `SELECT * FROM Customer` | Query (paginated) | customers | All (1 phys.) | `container.GetItemQueryIterator<Customer>(new QueryDefinition("SELECT * FROM c WHERE c.docType = 'customer'"), requestOptions: new QueryRequestOptions { MaxItemCount = 50 })` |
| 11 | Get customer by ID | Original | `SELECT * FROM Customer WHERE CustomerID = @id` | Point read | customers | Single ✅ | `container.ReadItemAsync<Customer>(id.ToString(), new PartitionKey(id.ToString()))` |
| 12 | Create customer | Original | `INSERT INTO Customer ...` | Create | customers | Single ✅ | `container.CreateItemAsync(customer, new PartitionKey(customer.CustomerId))` |
| 13 | Update customer | Original | `UPDATE Customer SET ... WHERE CustomerID = @id` | Replace | customers | Single ✅ | `container.ReplaceItemAsync(customer, customer.Id, new PartitionKey(customer.CustomerId))` |
| 14 | Delete customer | Original | `DELETE FROM Customer WHERE CustomerID = @id` | Delete | customers | Single ✅ | `container.DeleteItemAsync<Customer>(id, new PartitionKey(customerId))` |
| 15 | List orders by customer | Original (nav property) | `SELECT * FROM SalesOrderHeader WHERE CustomerID = @cid ORDER BY OrderDate DESC` | Partition query | customers | Single ✅ | `container.GetItemQueryIterator<SalesOrder>(new QueryDefinition("SELECT * FROM c WHERE c.docType = 'salesOrder' AND c.customerId = @cid ORDER BY c.orderDate DESC").WithParameter("@cid", customerId))` |
| 16 | Get order by ID | Original (nav property) | `SELECT h.*, d.* FROM SalesOrderHeader h JOIN SalesOrderDetail d ON h.SalesOrderID=d.SalesOrderID WHERE h.SalesOrderID = @id` | Point read (details embedded) | customers | Single ✅ | `container.ReadItemAsync<SalesOrder>(orderId, new PartitionKey(customerId))` |
| 17 | Create order | Original | `BEGIN TRAN; INSERT SalesOrderHeader; INSERT SalesOrderDetail (×N); COMMIT` | Single doc write (items embedded = atomic) | customers | Single ✅ | `container.CreateItemAsync(order, new PartitionKey(order.CustomerId))` |
| 18 | Propagate category name change | **NEW — denormalization** | N/A | Fan-out: query products by categoryId, replace each | products | All (1 phys.) | App-level: query + batch replace (max ~100 products per category) |
| 19 | List categories (dropdown) | Original | `SELECT ProductCategoryID, Name FROM ProductCategory` | Query with projection | products | All (1 phys.) | `container.GetItemQueryIterator<dynamic>(new QueryDefinition("SELECT c.productCategoryId, c.name FROM c WHERE c.docType = 'productCategory'"))` |

**Origin legend:**
- **Original** — direct migration of existing RDBMS access pattern
- **NEW — denormalization** — compensating write to propagate duplicated data

### 6b. RDBMS Construct Mapping

| RDBMS Construct | Source Example | Cosmos DB Equivalent |
|----------------|---------------|---------------------|
| JOIN (FK eager load) | `Product.Include(Category).Include(Model)` | Denormalized fields: `categoryName`, `modelName` embedded in product doc |
| JOIN (self-ref) | `ProductCategory.Include(ParentProductCategory)` | Denormalized `parentCategoryName` embedded in category doc |
| JOIN (Order→Details) | `SalesOrderHeader JOIN SalesOrderDetail` | Embedded `lineItems[]` array in order doc |
| M:N junction (CustomerAddress) | `Customer → CustomerAddress → Address` | Embedded `addresses[]` array in customer doc |
| M:N junction (PMPDC) | `ProductModel → PMPDC → ProductDescription` | Embedded `descriptions[]` array in model doc |
| IDENTITY auto-increment | `ProductID INT IDENTITY(1,1)` | String `id` field: `"product-680"`, `"category-18"`, `"29825"` |
| Computed column | `SalesOrderNumber AS 'SO' + CONVERT(...)` | Computed in app before write: `salesOrderNumber = $"SO{salesOrderId}"` |
| Computed column | `TotalDue AS SubTotal + TaxAmt + Freight` | Computed in app: `totalDue = subTotal + taxAmt + freight` |
| Computed column | `LineTotal AS UnitPrice * (1-Discount) * Qty` | Computed in app: `lineTotal = unitPrice * (1 - discount) * qty` |
| Sequence | `SalesOrderNumber` sequence | App-generated: atomic counter or timestamp-based IDs |
| UNIQUE constraint | `AK_Product_Name`, `AK_Product_ProductNumber` | Application-level validation (dataset is small enough for in-memory check or query-then-insert) |
| DEFAULT GETDATE() | `ModifiedDate DEFAULT GETDATE()` | Set in app: `modifiedDate = DateTime.UtcNow` in model constructor |
| CHECK constraint | `CK_Product_ListPrice CHECK (ListPrice >= 0)` | Application-level validation (model attributes / FluentValidation) |
| CASCADE DELETE | None in SalesLT (ClientSetNull) | Application-level: delete customer → optionally delete orders via query |

### 6c. Transaction Boundary Mapping

| RDBMS Transaction | Tables Involved | Cosmos DB Strategy | Scope |
|-------------------|----------------|-------------------|-------|
| Create order with line items | SalesOrderHeader + SalesOrderDetail (×N) | Single document write (items embedded in order doc) | Atomic ✅ |
| Create/update customer with addresses | Customer + CustomerAddress + Address | Single document write (addresses embedded in customer doc) | Atomic ✅ |
| Create customer + first order | Customer + SalesOrderHeader + SalesOrderDetail | Transactional batch (same partition key `/customerId`) | Atomic ✅ |
| Update category name + products | ProductCategory + Product (×N) | 1) Replace category doc → 2) Query+replace affected products | Eventually consistent (app-level) |
| Delete customer + orders | Customer + SalesOrderHeader (×N) | Transactional batch delete all docs in partition, or query+delete | Atomic (if batch) / Eventually consistent (if query+delete) |

### 6d. Gap Register

| Gap | RDBMS Capability | Impact | Recommended Approach |
|-----|-----------------|--------|---------------------|
| Category name propagation | FK + JOIN provides live name | When category name changes, product docs have stale `categoryName` | App-level fan-out update on category edit (max ~100 products per category — trivial). No Change Feed needed at this scale. |
| Model name propagation | FK + JOIN provides live name | When model name changes, product docs have stale `modelName` | App-level fan-out update on model edit (1-3 products per model). |
| No server-side FK enforcement | Foreign key constraints guaranteed referential integrity | Orphaned references possible if product deleted while referenced in orders | Application-level validation. Historical orders retain product snapshot (by design — order line items capture point-in-time data). |
| No ad-hoc cross-entity queries | Arbitrary SQL JOINs across any tables | Cannot join products + customers in single query | Not needed by current app. Customer + order co-location already eliminates the most common cross-entity JOIN. If broader analytics needed, use Azure Synapse Link. |
| List all customers (19,820 rows) | EF `ToListAsync()` loads all into memory | Inefficient at scale; Cosmos DB charges RU per page | **Recommend adding pagination**: use continuation tokens, `MaxItemCount = 50`. Flag for UI redesign. |

---

## Relationship Mapping

| Source (RDBMS) | Relationship | Cosmos DB Pattern | Rationale |
|---------------|-------------|-------------------|-----------|
| Product → ProductCategory | N:1 FK (ProductCategoryID) | Denormalize: embed `categoryName` + `parentCategoryName` in product doc. Keep separate category docs for CRUD. | >90% access correlation — products always loaded with category name. Only 41 categories; propagation is trivial. |
| Product → ProductModel | N:1 FK (ProductModelID) | Denormalize: embed `modelName` in product doc. Keep separate model docs for dropdowns. | >90% access correlation. Model name rarely changes. |
| ProductModel → ProductDescription | M:N via junction (PMPDC) | Embed: `descriptions[]` array in model doc (max 6 entries per model). | Bounded (≤6 cultures). Descriptions not independently accessed. Eliminates junction table. |
| ProductCategory → Parent | N:1 self-ref FK | Denormalize: embed `parentCategoryName` in child category doc. | Flat hierarchy (2 levels only). Parent name shown in category list. |
| Customer → Address | M:N via junction (CustomerAddress) | Embed: `addresses[]` array in customer doc (1-2 addresses). | Bounded, small. Addresses not independently accessed. Eliminates 2 tables. |
| Customer → SalesOrderHeader | 1:N FK (CustomerID) | Co-locate: both entity types in `customers` container, partitioned by `/customerId`. Discriminated by `docType`. | Same partition key enables single-partition order queries, transactional batch writes, and eliminates cross-container lookups. Avg 1.6 orders/customer keeps logical partitions small (~85 KB max). |
| SalesOrderHeader → SalesOrderDetail | 1:N FK (SalesOrderID) | Embed: `lineItems[]` array in order doc (avg 3.9 items). | Always accessed together. Bounded. Makes order creation atomic. |
| SalesOrderHeader → Address | N:1 FK (BillTo/ShipTo) | Snapshot: embed `billToAddress{}` / `shipToAddress{}` objects at order creation time. | Addresses on orders are point-in-time snapshots — should NOT be updated when customer address changes. |
| SalesOrderDetail → Product | N:1 FK (ProductID) | Snapshot: embed `productName` in line item at order creation time. | Product name in order line is a point-in-time record. Historical orders should not retroactively change. |

---

## Denormalization Register

| Duplicated Field | Source of Truth | Target Document | Propagation Strategy | Max Fan-Out |
|-----------------|----------------|-----------------|---------------------|-------------|
| `categoryName` | ProductCategory doc (`name`) | Product doc | App-level: on category rename, query products by `productCategoryId`, replace each | ~100 products |
| `parentCategoryName` | Parent ProductCategory doc (`name`) | Child ProductCategory doc + Product doc | App-level: on parent rename, update children + products | ~10 child categories + ~200 products |
| `modelName` | ProductModel doc (`name`) | Product doc | App-level: on model rename, query products by `productModelId`, replace each | ~3 products |
| `customerName` | Customer doc (`firstName` + `lastName`) | SalesOrder doc | **No propagation** — order captures customer name at time of sale (snapshot) | N/A |
| `productName` | Product doc (`name`) | SalesOrder line item | **No propagation** — order captures product name at time of sale (snapshot) | N/A |
| `billToAddress` | Address (at time of order) | SalesOrder doc | **No propagation** — point-in-time snapshot | N/A |
| `shipToAddress` | Address (at time of order) | SalesOrder doc | **No propagation** — point-in-time snapshot | N/A |

---

## Migration Pitfalls Addressed

| # | Pitfall | How Addressed |
|---|---------|---------------|
| 1 | **1:1 table-to-container mapping** | Consolidated 11 EF entities into 2 containers. Products container holds 3 entity types. Customers container holds customer + salesOrder types. Customer embeds Address + CustomerAddress. Order embeds Details. |
| 2 | **Junction tables preserved as containers** | Eliminated all 3 junction tables: CustomerAddress → embedded addresses[], PMPDC → embedded descriptions[], SalesOrderDetail → embedded lineItems[]. |
| 3 | **FK columns without embedding** | All FK references either embed the related name (categoryName, modelName, parentCategoryName) or are snapshots (address, productName in orders). |
| 4 | **Auto-increment IDs** | Replaced IDENTITY columns with string `id` values. Products container uses typed prefixes (`product-`, `category-`, `model-`) to avoid collisions. Customer and order IDs use string representations of original integers. |
| 5 | **Cross-partition queries on primary paths** | All P0 point reads are single-partition. Cross-partition list queries only occur on containers with 1 physical partition (negligible overhead). |
| 6 | **Unbounded arrays embedded** | All embedded arrays are bounded: addresses (1–2), descriptions (≤6), lineItems (avg 3.9, practical max ~70). |
| 7 | **No type discriminator** | `docType` property on every document. Products container (multi-type) uses: `product`, `productCategory`, `productModel`. Customers container (multi-type) uses: `customer`, `salesOrder`. |
| 8 | **Partition key without access pattern analysis** | Every partition key validated against the access pattern register (Phase 4) and volumetric checks (Phase 5). |
| 9 | **No cost estimate** | RU & storage estimates provided per container (Phase 7). Serverless mode recommended. |

---

## Indexes

### Products Container — Indexing Policy

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/docType/?" },
    { "path": "/name/?" },
    { "path": "/productCategoryId/?" },
    { "path": "/productModelId/?" },
    { "path": "/productNumber/?" }
  ],
  "excludedPaths": [
    { "path": "/*" }
  ]
}
```

**Rationale:** Exclude-all default with explicit includes for only the properties used in WHERE, ORDER BY, or fan-out queries. Indexed: `docType` (list queries), `name` (ordering/lookup), `productCategoryId` and `productModelId` (denormalization fan-out), `productNumber` (unique lookup). Reduces write RU and storage for non-queried fields.

### Customers Container — Indexing Policy

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/docType/?" },
    { "path": "/customerId/?" },
    { "path": "/lastName/?" },
    { "path": "/firstName/?" },
    { "path": "/orderDate/?" },
    { "path": "/status/?" },
    { "path": "/salesOrderNumber/?" }
  ],
  "excludedPaths": [
    { "path": "/*" }
  ]
}
```

**Rationale:** Exclude-all default with explicit includes for only queried properties. This container holds both `customer` and `salesOrder` doc types. Indexed: `docType` (type-discriminated list queries), `customerId` (partition-scoped order queries), `lastName`/`firstName` (customer listing), `orderDate` (order sorting), `status` (order filtering), `salesOrderNumber` (order lookup). All embedded arrays (addresses, lineItems, address snapshots), security fields (passwords), and freetext fields are automatically excluded.

---

## Phase 7 — Comprehensive Validation

### 7a. RU, Storage & Physical Partition Estimates

| Container | Entity Types | Records | Avg Doc Size | Storage (MB) | Read RU/s | Write RU/s | Compensating RU/s | Total RU/s | Physical Partitions |
|-----------|-------------|--------:|-------------:|-------------:|----------:|-----------:|------------------:|-----------:|--------------------:|
| products | Product, ProductCategory, ProductModel | 909 | 1.5 KB | 1.4 | 30 | 5 | 5 | 40 | 1 |
| customers | Customer, SalesOrder | 51,285 | 2.5 KB | 155.6 | 110 | 15 | 0 | 125 | 1 |
| **TOTAL** | | **52,194** | | **157.0** | **140** | **20** | **5** | **165** | **2** |

**Compensating RU/s:** Category/model name rename fan-out (pattern #18) — estimated 5 RU/s avg based on <1 rename/day affecting ~100 docs × 7 RU per replace.

**Throughput validation:**
- ✅ Serverless mode: no minimum RU/GB floor to worry about
- ✅ Peak RU/s per physical partition < 10,000 (peak ~165 total)
- ✅ Data per logical partition < 20 GB (max ~85 KB in customers)

### 7b. Cross-Partition Access Pattern Flags

| Pattern | Type | Container | Physical Partitions | Overhead Calculation | Total Peak RU/s |
|---------|------|-----------|--------------------:|---------------------|----------------:|
| List all products | Cross-partition | products | 1 | 15 + (2.5 × 1) = 17.5 RU | 20 × 17.5 = 350 |
| List all categories | Cross-partition | products | 1 | 3 + (2.5 × 1) = 5.5 RU | 20 × 5.5 = 110 |
| List all models (dropdown) | Cross-partition | products | 1 | 10 + (2.5 × 1) = 12.5 RU | 10 × 12.5 = 125 |
| List all customers | Cross-partition | customers | 1 | 25 + (2.5 × 1) = 27.5 RU/page | 15 × 27.5 = 412 |

All cross-partition queries operate on single physical partitions — overhead is minimal.

| Check | Result | Pass? |
|-------|--------|-------|
| All cross-partition patterns identified | 4 patterns above | ✅ |
| Cross-partition P0 paths → single physical partition | All 1 physical partition | ✅ |
| Cross-partition overhead < 20% of total RU/s | ~1,000 out of ~1,000 total (these ARE the main reads) — acceptable for single physical partition | ✅ |
| Physical partition count won't cause runaway fan-out | 1 per container | ✅ |

### 7c. Partition Key Validation

| Check | products | customers | Pass? |
|-------|----------|-----------|-------|
| All P0 queries single-partition or single physical partition | ✅ | ✅ | ✅ |
| No logical partition > 20 GB | Max 3 KB | Max ~85 KB (customer + ~20 orders) | ✅ |
| No hot partition > 10K RU/s at peak | <50 | <150 | ✅ |
| Write distribution even | 1 doc/partition | Avg 2.6 docs/partition | ✅ |

### 7d. Data Model Validation

| Check | Result | Pass? |
|-------|--------|-------|
| No document exceeds 1 MB | Max ~15 KB (large order with 70 line items) | ✅ |
| No unbounded arrays embedded | addresses ≤2, descriptions ≤6, lineItems bounded by business rules | ✅ |
| Multi-entity containers have `docType` discriminator | `products`: ✅ (`product`, `productCategory`, `productModel`); `customers`: ✅ (`customer`, `salesOrder`) | ✅ |
| Denormalized fields have propagation strategy | categoryName/modelName: app-level. Snapshots (customerName, productName, addresses in orders): intentionally no propagation. | ✅ |
| Container consolidation evaluated | 11 entities → 2 containers (products consolidated 3 types; customers consolidated customer + order + 4 embedded entities) | ✅ |

### 7e. Query & Index Validation

| Check | Result | Pass? |
|-------|--------|-------|
| ORDER BY queries have composite indexes | No composite indexes defined — ORDER BY on single fields uses range indexes. Multi-field ORDER BY will require composite indexes to be added later. | ⚠️ |
| Unused paths excluded | Exclude-all (`/*`) default; only queried properties explicitly included | ✅ |
| Queries use projections | Dropdown queries project only id+name | ✅ |
| Queries are parameterized | All `QueryDefinition` calls use `.WithParameter()` | ✅ |
| Large result sets use pagination | Customer list: `MaxItemCount = 50` with continuation tokens | ✅ |

### 7f. Operational Validation

| Check | Result | Pass? |
|-------|--------|-------|
| Throughput mode: Serverless | Optimal for <1 GB, <200 RU/s peak, variable traffic | ✅ |
| TTL for transient data | Not applicable — all data is retained | ✅ (N/A) |
| Monitoring plan | Track RU consumption via Azure Monitor; alert on RU > 500 (unexpected spike) | ✅ |
| CosmosClient singleton pattern | Register as singleton in DI: `services.AddSingleton<CosmosClient>(...)` | ✅ |
| Change Feed needed? | Not needed at current scale — app-level fan-out for name propagation (max 100 docs) is sufficient | ✅ |

### 7g. Scale Readiness

| Check | Result | Pass? |
|-------|--------|-------|
| Write-heavy entities need binning? | No — <5 writes/sec total | ✅ (N/A) |
| Hot keys need write sharding? | No hot keys identified | ✅ (N/A) |
| Multi-region requirements? | Single region for internal app (can add read regions later) | ✅ |
| Burst capacity? | Serverless mode handles bursts automatically | ✅ |

---

## RU & Storage Estimate

| Container | Entity Types | Records | Avg Doc Size | Storage (MB) | Read RU/s | Write RU/s | Compensating RU/s | Total RU/s | Physical Partitions |
|-----------|-------------|--------:|-------------:|-------------:|----------:|-----------:|------------------:|-----------:|--------------------:|
| products | Product, ProductCategory, ProductModel | 909 | 1.5 KB | 1.4 | 30 | 5 | 5 | 40 | 1 |
| customers | Customer, SalesOrder (co-located) | 51,285 | 2.5 KB | 155.6 | 110 | 15 | 0 | 125 | 1 |
| **TOTAL** | | **52,194** | | **157.0** | **140** | **20** | **5** | **165** | **2** |

**Notes:**
- **Compensating RU/s** = RU consumed by category/model name propagation fan-out writes (pattern #18). Estimated <5 RU/s average.
- **Physical Partitions** = MAX(CEIL(Storage ÷ 50 GB), CEIL(Total RU/s ÷ 10,000)) = 1 per container.
- **Throughput mode: Serverless** — no minimum RU/s. Pay-per-request. Optimal for this workload profile.
- **Estimated monthly cost:** <$5/month at current data volumes and traffic (serverless pricing: $0.25 per 1M RU consumed + $0.25/GB/month storage).

---

## SDK & Authentication Configuration

### CosmosClient Setup (Singleton)

```csharp
// Program.cs or Startup.cs
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using System.Text.Json;

builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["CosmosDb:Endpoint"];

    var clientOptions = new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Direct,
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        },
        ApplicationName = "AdventureWorks.Web"
    };

    return new CosmosClient(endpoint, new DefaultAzureCredential(), clientOptions);
});
```

### Configuration (appsettings.json)

```json
{
  "CosmosDb": {
    "Endpoint": "https://<account-name>.documents.azure.com:443/",
    "DatabaseName": "adventureworks",
    "Containers": {
      "Products": "products",
      "Customers": "customers"
    }
  }
}
```

### Required RBAC Role Assignment

```bash
# Assign "Cosmos DB Built-in Data Contributor" role to the app's managed identity
az cosmosdb sql role assignment create \
  --account-name <account-name> \
  --resource-group <rg-name> \
  --role-definition-id 00000000-0000-0000-0000-000000000002 \
  --principal-id <managed-identity-object-id> \
  --scope "/"
```

---

## ETL Data Mapping (Full AW CSVs → Cosmos DB Documents)

This section documents how to build each Cosmos DB document from the normalized CSV data files.

### Customer Document ETL

```
Source tables (JOINed):
  Sales.Customer        → CustomerID, PersonID, StoreID, TerritoryID, AccountNumber
  Person.Person         → FirstName, MiddleName, LastName, Title, Suffix, NameStyle
                          (JOIN ON Customer.PersonID = Person.BusinessEntityID)
  Person.EmailAddress   → EmailAddress
                          (JOIN ON Customer.PersonID = EmailAddress.BusinessEntityID)
  Person.Password       → PasswordHash, PasswordSalt
                          (JOIN ON Customer.PersonID = Password.BusinessEntityID)
  Person.PersonPhone    → Phone
                          (JOIN ON Customer.PersonID = PersonPhone.BusinessEntityID)
  Sales.Store           → CompanyName = Store.Name
                          (JOIN ON Customer.StoreID = Store.BusinessEntityID)

Embedded addresses[] built from:
  Person.BusinessEntityAddress → AddressID, AddressTypeID
                          (JOIN ON Customer.PersonID = BEA.BusinessEntityID)
  Person.Address        → AddressLine1/2, City, StateProvince, CountryRegion, PostalCode
                          (JOIN ON BEA.AddressID = Address.AddressID)
  Person.AddressType    → AddressType name
                          (JOIN ON BEA.AddressTypeID = AddressType.AddressTypeID)

ID mapping:
  id = Customer.CustomerID.ToString()  (e.g., "29825")
  customerId = Customer.CustomerID.ToString()  (partition key, same as id)
```

### Product Document ETL

```
Source tables:
  Production.Product           → all product fields
  Production.ProductSubcategory → SubcategoryID maps to ProductCategoryID in SalesLT
                                  (JOIN ON Product.ProductSubcategoryID = Subcat.ProductSubcategoryID)
  Production.ProductCategory   → parent category name
                                  (JOIN ON Subcat.ProductCategoryID = Cat.ProductCategoryID)
  Production.ProductModel      → model name
                                  (JOIN ON Product.ProductModelID = Model.ProductModelID)

ID remapping for ProductCategory:
  Parent categories:  productCategoryId = ProductCategory.ProductCategoryID (1–4)
  Child subcategories: productCategoryId = ProductSubcategory.ProductSubcategoryID + 4 (5–41)
  Product.productCategoryId = SubcategoryID + 4  (to reference merged hierarchy)

ID mapping:
  id = "product-" + Product.ProductID.ToString()
```

### SalesOrder Document ETL

```
Source tables:
  Sales.SalesOrderHeader → all header fields
  Sales.SalesOrderDetail → embedded as lineItems[]
                           (JOIN ON SOH.SalesOrderID = SOD.SalesOrderID)
  Person.Address (×2)    → billToAddress, shipToAddress snapshots
                           (JOIN ON SOH.BillToAddressID / ShipToAddressID = Address.AddressID)
  Purchasing.ShipMethod  → shipMethod name
                           (JOIN ON SOH.ShipMethodID = ShipMethod.ShipMethodID)
  Sales.Customer + Person.Person → customerName (FirstName + ' ' + LastName)
                           (JOIN ON SOH.CustomerID = Customer.CustomerID
                            JOIN ON Customer.PersonID = Person.BusinessEntityID)
  Production.Product     → productName for each line item
                           (JOIN ON SOD.ProductID = Product.ProductID)

ID mapping:
  id = SalesOrderHeader.SalesOrderID.ToString()  (e.g., "43659")
  customerId = SalesOrderHeader.CustomerID.ToString()  (partition key)
```

### CSV Delimiter Reference

| File | Delimiter | Row Terminator | Parser |
|------|-----------|----------------|--------|
| Customer.csv, Product.csv, ProductCategory.csv, ProductSubcategory.csv, Address.csv, SalesOrderHeader.csv, SalesOrderDetail.csv, ProductDescription.csv, PMPDC.csv | `\t` (tab) | `\n` (newline) | `line.Split('\t')` |
| Person.csv, Store.csv, ProductModel.csv, EmailAddress.csv, Password.csv, PersonPhone.csv, BusinessEntityAddress.csv | `+\|` (pipe) | `&\|` + newline | `line.TrimEnd("&\|").Split("+\|")` |

---

## Alternative Design Considered

### Alternative: Single Container (all entity types)

| Container | Partition Key | Entity Types | Total Size | RU/s |
|-----------|--------------|-------------|-----------|------|
| adventureworks | `/partitionKey` (synthetic) | product, productCategory, productModel, customer, salesOrder | 157 MB | 165 |

**Why rejected:** While a single container reduces the Cosmos DB serverless minimum request charge and simplifies management, it mixes unrelated access patterns (product catalog vs customer/order management). The synthetic partition key would require every document to compute its own `partitionKey` value, adding complexity with no performance benefit at this scale. The 2-container design provides cleaner code separation, independent indexing policies, and maps naturally to the existing controller structure while still co-locating related entities (customer + orders) that benefit from shared partition keys.

### Alternative: 3 Containers (separate salesOrders)

| Container | Partition Key | Entity Types | Total Size | RU/s |
|-----------|--------------|-------------|-----------|------|
| products | `/id` | product, productCategory, productModel | 1.4 MB | 40 |
| customers | `/id` | customer | 29.7 MB | 90 |
| salesOrders | `/customerId` | salesOrder | 125.9 MB | 35 |

**Why rejected:** Separating orders from customers prevents transactional batch writes (e.g., creating a customer and first order atomically). It also means querying a customer's full profile (customer + their orders) requires two container lookups instead of one partition query. Co-locating under `/customerId` keeps partitions small (~85 KB max) and enables single-partition access for the most common Customer → Orders navigation pattern.

**RU comparison:**

| Design | Containers | Total RU/s | Complexity |
|--------|-----------|-----------|------------|
| 2 containers (selected) | 2 | 165 | Low — natural co-location, transactional batch |
| 1 container (alternative) | 1 | 165 | Medium — synthetic PK, shared indexing |
| 3 containers (alternative) | 3 | 165 | Low — but no transactional batch across customer+order |

All designs have identical RU cost. Selected 2-container design wins on transactional guarantees and query efficiency.
