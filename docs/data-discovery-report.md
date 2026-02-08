# Data Discovery Report: AdventureWorks.Web → Cosmos DB Migration

**Date:** 2026-02-07  
**Scope:** Pre-migration analysis of ASP.NET Core MVC app backed by SQL Server (AdventureWorks schema)

---

## 1. Application Overview

| Attribute | Value |
|---|---|
| Framework | ASP.NET Core 2.1 (MVC) |
| ORM | Entity Framework Core (SQL Server) |
| DbContext | `sampledbContext` |
| EF Schema | `SalesLT.*` (AdventureWorks **Lightweight**) |
| CSV Data Schema | Full AdventureWorks (`Sales.*`, `Production.*`, `Person.*`) |
| Default Route | `ProductCategories/Index` |
| Controllers | `HomeController`, `CustomersController`, `ProductsController`, `ProductCategoriesController` |

### Key Mismatch: EF Models vs. CSV Data

The EF models map to the **SalesLT** schema (AdventureWorksLT), which is a simplified, denormalized version. The CSV data files and `instawdb.sql` use the **full** AdventureWorks schema (71 tables across `Sales`, `Production`, `Person`, `HumanResources`, `Purchasing` schemas). This means:

- **SalesLT.Customer** (EF model) = denormalized merge of `Sales.Customer` + `Person.Person` + `Person.EmailAddress` + `Person.Password` + `Person.PersonPhone`
- **SalesLT.Product** (EF model) ≈ `Production.Product` but with `ProductCategoryID` instead of `ProductSubcategoryID`
- **SalesLT.ProductCategory** (EF model) = flattened hierarchy from `Production.ProductCategory` + `Production.ProductSubcategory`
- **SalesLT.SalesOrderHeader** (EF model) = simplified `Sales.SalesOrderHeader` (fewer FK columns)
- **SalesLT.SalesOrderDetail** (EF model) = simplified `Sales.SalesOrderDetail` (no `SpecialOfferID`)

---

## 2. CSV Data File Analysis

### 2.1 Delimiter Formats

| Format | Delimiter | Row Terminator | Files |
|---|---|---|---|
| **Tab-delimited** | `\t` | newline | Customer, Product, ProductCategory, ProductSubcategory, Address, SalesOrderHeader, SalesOrderDetail, ProductDescription, ProductModelProductDescriptionCulture |
| **Pipe-delimited** | `+\|` | `&\|` + newline | Person, Store, ProductModel, EmailAddress, Password, PersonPhone, BusinessEntityAddress |

### 2.2 Row Counts & Column Counts

| CSV File | Rows | Columns | Delimiter | SQL Table |
|---|---|---|---|---|
| Customer.csv | 19,820 | 7 (tab) | tab | `Sales.Customer` |
| Person.csv | 19,972 | 12 (pipe) | `+\|` | `Person.Person` |
| Address.csv | 19,614 | 9 (tab) | tab | `Person.Address` |
| BusinessEntityAddress.csv | 19,614 | 5 (pipe) | `+\|` | `Person.BusinessEntityAddress` |
| EmailAddress.csv | – | 5 (pipe) | `+\|` | `Person.EmailAddress` |
| Password.csv | – | 5 (pipe) | `+\|` | `Person.Password` |
| PersonPhone.csv | – | 4 (pipe) | `+\|` | `Person.PersonPhone` |
| Store.csv | 701 | 6 (pipe) | `+\|` | `Sales.Store` |
| Product.csv | 504 | 25 (tab) | tab | `Production.Product` |
| ProductCategory.csv | 4 | 4 (tab) | tab | `Production.ProductCategory` |
| ProductSubcategory.csv | 37 | 5 (tab) | tab | `Production.ProductSubcategory` |
| ProductModel.csv | 364 | 6 (pipe) | `+\|` | `Production.ProductModel` |
| ProductDescription.csv | 762 | 4 (tab) | tab | `Production.ProductDescription` |
| ProductModelProductDescriptionCulture.csv | 762 | 4 (tab) | tab | `Production.ProductModelProductDescriptionCulture` |
| SalesOrderHeader.csv | 31,465 | 26 (tab) | tab | `Sales.SalesOrderHeader` |
| SalesOrderDetail.csv | 121,317 | 11 (tab) | tab | `Sales.SalesOrderDetail` |

### 2.3 ID Ranges

| Entity | ID Column | Min | Max | Notes |
|---|---|---|---|---|
| Customer | CustomerID | 1 | 30,118 | Sparse — 19,820 rows in range 1–30,118 |
| Address | AddressID | 1 | 32,521 | Sparse — 19,614 rows |
| Product | ProductID | 1 | 999+ | 504 rows |
| ProductCategory | ProductCategoryID | 1 | 4 | Only 4 top-level categories |
| ProductSubcategory | ProductSubcategoryID | 1 | 37 | 37 subcategories |
| SalesOrderHeader | SalesOrderID | 43,659 | 75,123 | 31,465 rows |
| SalesOrderDetail | SalesOrderDetailID | auto | auto | 121,317 rows |
| ProductModel | ProductModelID | 1 | 128+ | 364 rows (pipe-delimited, 6 cols) |

### 2.4 SQL Schema Column Mappings vs. CSV

**Sales.Customer (7 columns in CSV)**
| Col # | SQL Column | Sample Value |
|---|---|---|
| 0 | CustomerID | 1 |
| 1 | PersonID (nullable) | 934 |
| 2 | StoreID (nullable) | _(empty)_ or integer |
| 3 | TerritoryID | 1 |
| 4 | AccountNumber (computed) | AW00000001 |
| 5 | rowguid | {GUID} |
| 6 | ModifiedDate | 2014-09-12 ... |

**Production.Product (25 columns in CSV)**
| Col # | SQL Column | Col # | SQL Column |
|---|---|---|---|
| 0 | ProductID | 13 | Weight |
| 1 | Name | 14 | DaysToManufacture |
| 2 | ProductNumber | 15 | ProductLine |
| 3 | MakeFlag | 16 | Class |
| 4 | FinishedGoodsFlag | 17 | Style |
| 5 | Color | 18 | ProductSubcategoryID |
| 6 | SafetyStockLevel | 19 | ProductModelID |
| 7 | ReorderPoint | 20 | SellStartDate |
| 8 | StandardCost | 21 | SellEndDate |
| 9 | ListPrice | 22 | DiscontinuedDate |
| 10 | Size | 23 | rowguid |
| 11 | SizeUnitMeasureCode | 24 | ModifiedDate |
| 12 | WeightUnitMeasureCode | | |

---

## 3. Data Subsets

### 3.1 Customer Categories

| Category | Count | Description |
|---|---|---|
| PersonID only | 18,484 | Individual customers (no store affiliation) |
| StoreID only | 701 | Store-only accounts (no person) |
| Both PersonID + StoreID | 635 | People associated with a store |
| Neither | 0 | All customers have at least one reference |
| **Total** | **19,820** | |

### 3.2 Customers with Orders

| Metric | Value |
|---|---|
| Distinct customers with orders | 19,119 |
| Customers without orders | ~701 |
| Total sales orders | 31,465 |
| Total order line items | 121,317 |

### 3.3 Product Coverage

| Metric | Value |
|---|---|
| Total products | 504 |
| Products with SubcategoryID | 295 (58.5%) |
| Products with ProductModelID | 295 (58.5%) — same set |
| Products without subcategory/model | 209 (41.5%) — raw materials, etc. |
| Distinct products in sales orders | 266 |

### 3.4 Product Description Cultures

6 cultures in ProductModelProductDescriptionCulture:
`en`, `fr`, `zh-cht`, `ar`, `he`, `th`

762 total rows across 6 cultures → ~127 product models × 6 cultures.

---

## 4. Entity Relationship Map: Tables Needed per EF Entity

### 4.1 EF Entity → Required Source Tables (Full AW → SalesLT Mapping)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.Customer (EF Model)                                          │
│  ← Sales.Customer (CustomerID, PersonID, StoreID, TerritoryID)        │
│  ← Person.Person  (FirstName, MiddleName, LastName, Title, Suffix,    │
│                     NameStyle via BusinessEntityID = PersonID)         │
│  ← Person.EmailAddress (EmailAddress via BusinessEntityID)            │
│  ← Person.Password     (PasswordHash, PasswordSalt via BEID)         │
│  ← Person.PersonPhone  (Phone via BusinessEntityID)                   │
│  ← Sales.Store          (CompanyName = Store.Name, when StoreID set)  │
│  Optional: SalesPerson name from Sales.SalesPerson                    │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.Product (EF Model)                                           │
│  ← Production.Product  (all product fields)                           │
│  ← Production.ProductSubcategory (maps SubcategoryID → CategoryID)    │
│  Note: EF model has ProductCategoryID directly = SubcategoryID        │
│        in the full AW schema (categories are really subcategories)     │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.ProductCategory (EF Model — parent/child hierarchy)          │
│  ← Production.ProductCategory    (parent: Bikes, Components, etc.)    │
│  ← Production.ProductSubcategory (child: Mountain Bikes, etc.)        │
│  Merged into one table with ParentProductCategoryID = parent cat ID   │
│  Parent rows: IDs 1–4 (Bikes, Components, Clothing, Accessories)      │
│  Child rows:  IDs 5–41 (= SubcategoryID + 4 offset, or renumbered)   │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.ProductModel (EF Model)                                      │
│  ← Production.ProductModel (ProductModelID, Name, CatalogDescription) │
│  Straightforward 1:1 mapping                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.ProductDescription (EF Model)                                │
│  ← Production.ProductDescription (ProductDescriptionID, Description)  │
│  Straightforward 1:1 mapping                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.ProductModelProductDescription (EF Model — junction table)   │
│  ← Production.ProductModelProductDescriptionCulture                   │
│  Composite key: (ProductModelID, ProductDescriptionID, Culture)       │
│  Straightforward 1:1 mapping                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.Address (EF Model)                                           │
│  ← Person.Address (AddressID, AddressLine1/2, City, StateProvince,    │
│                     CountryRegion, PostalCode)                        │
│  Note: CSV has 9 cols (includes SpatialLocation not in EF model)      │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.CustomerAddress (EF Model — junction table)                  │
│  ← Person.BusinessEntityAddress (BusinessEntityID→CustomerID map)     │
│  Note: BEA uses AddressTypeID (FK) vs SalesLT uses AddressType (Name)│
│  Will need join to Person.AddressType to get the type name            │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.SalesOrderHeader (EF Model)                                  │
│  ← Sales.SalesOrderHeader                                             │
│  EF model: ShipMethod = string (SalesLT has name)                     │
│  Full AW:  ShipMethodID = FK → Purchasing.ShipMethod.Name             │
│  EF model omits: SalesPersonID, TerritoryID, ShipMethodID,            │
│                   CreditCardID, CurrencyRateID                        │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  SalesLT.SalesOrderDetail (EF Model)                                  │
│  ← Sales.SalesOrderDetail                                             │
│  EF model omits: CarrierTrackingNumber, SpecialOfferID                │
│  Otherwise 1:1 mapping                                                │
└─────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Join Graph for Data Migration ETL

```
Person.Person ──(BusinessEntityID)──┐
Person.EmailAddress ────────────────┤
Person.Password ────────────────────┤
Person.PersonPhone ─────────────────┤
                                    ├──→ JOIN on BusinessEntityID = PersonID
Sales.Customer ─────(PersonID)──────┘
       │
       ├──(StoreID)──→ Sales.Store (CompanyName)
       │
       └──(CustomerID)──→ Sales.SalesOrderHeader
                                │
                                ├──(BillToAddressID)──→ Person.Address
                                ├──(ShipToAddressID)──→ Person.Address
                                ├──(ShipMethodID)──→ Purchasing.ShipMethod
                                │
                                └──→ Sales.SalesOrderDetail
                                        │
                                        └──(ProductID)──→ Production.Product
                                                              │
                                                              ├──(ProductSubcategoryID)──→ Production.ProductSubcategory
                                                              │                               │
                                                              │                               └──(ProductCategoryID)──→ Production.ProductCategory
                                                              │
                                                              └──(ProductModelID)──→ Production.ProductModel
                                                                                        │
                                                                                        └──→ Production.ProductModelProductDescriptionCulture
                                                                                                │
                                                                                                └──(ProductDescriptionID)──→ Production.ProductDescription
```

---

## 5. ID Overlap Analysis (for Entity Merging)

### ProductCategory + ProductSubcategory → Merged ProductCategory

| Source | ID Range | Count |
|---|---|---|
| Production.ProductCategory | 1–4 | 4 |
| Production.ProductSubcategory | 1–37 | 37 |
| **OVERLAP**: IDs 1–4 exist in **both** tables | | |

**Action Required**: When merging into SalesLT.ProductCategory, subcategory IDs must be **renumbered** (e.g., offset by +4 or reassigned) to avoid collision with parent category IDs 1–4.  
The SalesLT version typically assigns parent categories IDs 1–4 and subcategories IDs 5–41.

### Customer.PersonID → Person.BusinessEntityID

| Entity | ID Range | Count |
|---|---|---|
| Sales.Customer.CustomerID | 1–30,118 | 19,820 |
| Sales.Customer.PersonID | References Person BEID | 18,484 + 635 = 19,119 with PersonID |

No overlap issue — different ID spaces (CustomerID vs PersonID/BusinessEntityID).

### Address Overlap with SalesOrderHeader

SalesOrderHeader references BillToAddressID and ShipToAddressID from Address (range 1–32,521). These are validated FK references — no overlap concern.

---

## 6. Tables Required for Migration (Minimum Set)

### Actively Used by App Controllers

| EF Entity | Controller CRUD | Queries Used |
|---|---|---|
| ProductCategory | Full CRUD | `.Include(ParentProductCategory)` |
| Product | Full CRUD | `.Include(ProductCategory).Include(ProductModel)` |
| Customer | Full CRUD | `.ToListAsync()` (flat, no includes) |
| SalesOrderHeader | _Not exposed_ | Referenced in Customer model (nav property) |
| SalesOrderDetail | _Not exposed_ | Referenced in SalesOrderHeader model |
| Address | _Not exposed_ | Referenced via CustomerAddress, SalesOrderHeader |
| CustomerAddress | _Not exposed_ | Referenced in Customer model |
| ProductModel | Used in dropdowns | `SelectList` in Products Create/Edit |
| ProductDescription | _Not exposed_ | Via ProductModelProductDescription |
| ProductModelProductDescription | _Not exposed_ | Junction table |

### Source CSV Tables Needed

**Must have (core app entities):**
1. `Sales.Customer` → Customer.csv
2. `Person.Person` → Person.csv
3. `Person.EmailAddress` → EmailAddress.csv
4. `Person.Password` → Password.csv
5. `Person.PersonPhone` → PersonPhone.csv
6. `Sales.Store` → Store.csv
7. `Production.Product` → Product.csv
8. `Production.ProductCategory` → ProductCategory.csv
9. `Production.ProductSubcategory` → ProductSubcategory.csv
10. `Production.ProductModel` → ProductModel.csv
11. `Production.ProductDescription` → ProductDescription.csv
12. `Production.ProductModelProductDescriptionCulture` → ProductModelProductDescriptionCulture.csv
13. `Person.Address` → Address.csv
14. `Person.BusinessEntityAddress` → BusinessEntityAddress.csv
15. `Sales.SalesOrderHeader` → SalesOrderHeader.csv
16. `Sales.SalesOrderDetail` → SalesOrderDetail.csv

**Also needed for FK resolution:**
17. `Person.AddressType` → AddressType.csv (to resolve AddressTypeID → name)
18. `Purchasing.ShipMethod` → ShipMethod.csv (to resolve ShipMethodID → name)

**Not needed (EF model entities not backed by data/UI):**
- `BuildVersion` — metadata only
- `ErrorLog` — operational, not migrated

---

## 7. Summary & Migration Risks

### Key Findings

1. **Schema mismatch is the #1 risk**: The EF models expect SalesLT (denormalized) but CSVs provide full AW (normalized). A multi-table JOIN/ETL is needed to materialize each SalesLT entity.

2. **Customer entity is the most complex merge**: Requires joining 5–6 source tables (Customer + Person + EmailAddress + Password + PersonPhone + Store).

3. **ProductCategory requires ID remapping**: ProductCategory (4 rows) and ProductSubcategory (37 rows) have overlapping ID ranges (1–4) and must be merged with offset IDs.

4. **Data volume is moderate**: ~20K customers, 500 products, 31K orders, 121K line items — well within Cosmos DB's capabilities.

5. **266 products are referenced in orders** out of 504 total — the remaining 238 are catalog-only products (mostly components/raw materials without subcategories).

6. **19,119 of 19,820 customers have orders** — only ~701 store-only accounts lack order history.

7. **Two different CSV delimiter formats** must be handled: tab-delimited and `+|`/`&|` pipe-delimited.

### Recommended Next Steps

1. Design Cosmos DB container model (likely 2–3 containers with denormalized documents)
2. Build ETL pipeline to join full AW CSVs → SalesLT-shaped entities
3. Upgrade from .NET Core 2.1 to .NET 8+ with Cosmos DB SDK
4. Replace EF Core SQL Server with Cosmos DB provider or direct SDK calls
5. Redesign data access patterns around partition keys (e.g., CustomerId for orders)
