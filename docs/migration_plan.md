# Holdings RDBMS → Azure Cosmos DB NoSQL Migration Assessment

> **Domain:** Temenos Holdings Microservice  
> **Source:** Relational (JPA/Hibernate entities with `ms_holdings_*` tables)  
> **Target:** Azure Cosmos DB for NoSQL  
> **Date:** 2026-02-25  

---

## Table of Contents

1. [Relational Tables Inventory](#1-relational-tables-inventory)
2. [Access-Patterns Inventory](#2-access-patterns-inventory)
3. [Cosmos DB Container Design](#3-cosmos-db-container-design)
   - 3.1 [Container Mappings Summary](#31-container-mappings-summary)
   - 3.2 [JSON Document Models](#32-json-document-models)
   - 3.3 [Indexing Policies](#33-indexing-policies)
4. [Access-Pattern → SDK-Call Mapping](#4-access-pattern--sdk-call-mapping)
5. [Partition Key Validation](#5-partition-key-validation)
6. [Cross-Partition Pattern Analysis](#6-cross-partition-pattern-analysis)
7. [RU & Storage Estimates](#7-ru--storage-estimates)
8. [Relationship Mapping](#8-relationship-mapping)
9. [Denormalization Register](#9-denormalization-register)
10. [Validation Checklists](#10-validation-checklists)
11. [Migration Pitfalls Addressed](#11-migration-pitfalls-addressed)
12. [Sample Document Analysis & Design Revision](#12-sample-document-analysis--design-revision) ⚠️ NEW
13. [Spring Boot Integration Recommendations](#13-spring-boot-integration-recommendations) ⚠️ NEW

---

## 1. Relational Tables Inventory

> Row counts marked with **★** are provided from source data. All others are **guesstimated** based on domain assumptions (15M customers, 32.5M arrangements, typical banking ratios) and should be validated with the DBA team.

| # | Table | RDBMS Name | Type | Est. Row Count | Partition Key (Source) | Avg Row Size | Growth Rate | Notes |
|--:|-------|-----------|------|---------------:|----------------------|-------------:|-------------|-------|
| 1 | **Arrangement** | `ms_holdings_arrangement` | Entity (Root Aggregate) | **32,500,000 ★** | `arrangementId` | **~187 KB (with bills) / ~3 KB (core only)** | +500K/mo | Core entity. Has 14 embeddable children. 25M active + 7.5M historical. **⚠️ Real sample shows 187 KB due to unbounded `arrangementBills` array (472 items, 183.5 KB). Core doc without bills is ~3 KB. Bills MUST be extracted — see Section 12.** |
| 2 | Arrangement ↳ Embeddables (×14) | N/A (embedded columns) | @Embeddable | — | — | — | — | PortfolioArr, AccountArr, LendingArr, DepositArr, EmailAddresses (→EmailAddressType, GenericNamesType), ~~ArrangementBills (→BillTypeDetails)~~ **⚠️ EXTRACTED — now separate `arrangementBill` docs**, PostingRestrictArrangement (→PostingRestrictDetails), CorrespondenceAddresses (→PostalAddressType, GenericNamesType), ArrangementInterest, ScheduleDetails, AccountServices, CompanyDetails, ProductDetails, OfficerDetails |
| 3 | **PartyArrangement** | `ms_holdings_partyArrangement` | Entity (Junction M:N) | 65,000,000 | `recId` | 0.4 KB | +1M/mo | ~2 parties per arrangement avg. Links Arrangement ↔ PartyDetails. Fields: arrangementId, partyId, partyRole, isPartyOwner, isActive |
| 4 | **PartyDetails** | `ms_holdings_partyDetails` | Entity | **15,000,000 ★** | `partyId` | 2.5 KB | +200K/mo | Customer/party master. Has embedded PostingRestrictDetails. |
| 5 | **Transaction** | `ms_holdings_transaction` | Entity | **100,000,000 ★** | `accountId` | 1.5 KB | +5M/mo | Highest volume table. Indexes on processingDate, transactionAmount, narrative, categorisationId, businessKey. |
| 6 | **Balance** | `ms_holdings_balance` | Entity | **100,000,000 ★** | `accountId` | 1.0 KB | +500K/mo | One-to-few per account. businessKey indexed. |
| 7 | BusinessContractActivity | `ms_holdings_businessContractActivity` | Entity | 30,000,000 | `activityId` | 1.5 KB | +300K/mo | Queried by (systemId, companyId, contractId, businessKey). Has ContextDetails embedded. |
| 8 | BusinessContractBalance | `ms_holdings_businessContractBalance` | Entity | 30,000,000 | `entityId` | 1.0 KB | +300K/mo | Queried by (systemId, companyId, contractId, businessKey). |
| 9 | DueDiligence | `ms_holdings_dueDiligence` | Entity | 5,000,000 | `arrangementId` | 2.0 KB | +50K/mo | KYC/AML data. Has TransactionalIntensions embedded. |
| 10 | PostingRestrictDetails | `ms_holdings_postingRestrictDetails` | Entity | 1,000,000 | — | 0.5 KB | +10K/mo | Referenced by arrangements and parties. |
| 11 | PaymentSchedules | `ms_holdings_paymentSchedules` | Entity | 10,000,000 | — | 1.0 KB | +200K/mo | Repayment/interest schedule data for lending arrangements. |
| 12 | MsAltKey | `ms_holdings_msAltKey` | Entity (Lookup) | 130,000,000 | — | 0.3 KB | +1M/mo | Alternate key lookup: accountId→arrangementId, businessKey→entityId, etc. ~3-4 per arrangement + ~2 per party. |
| 13 | ProductDetails | `ms_holdings_productDetails` | Entity | 10,000 | — | 1.5 KB | Rare | Product catalog reference. Has RetentionPeriod embedded. |
| 14 | ProductGroupDetails | `ms_holdings_productGroupDetails` | Entity | 500 | `productGroupIdentifier` | 1.0 KB | Rare | Product group reference. Has RetentionPeriod embedded. |
| 15 | OfficerDetails | `ms_holdings_officerDetails` | Entity | 500,000 | — | 0.8 KB | +5K/mo | Bank officers linked to arrangements. |
| 16 | CompanyDetails | `ms_holdings_companyDetails` | Entity | 100 | — | 1.0 KB | Rare | Company/branch reference. |
| 17 | CountryCodes | `ms_holdings_countryCodes` | Entity (Reference) | 250 | `code` | 0.5 KB | Rare | ISO country codes with CountrySubDivisions embedded. |
| 18 | TransactionType | `ms_holdings_transactionType` | Entity (Reference) | 500 | — | 0.4 KB | Rare | Transaction type catalog. |
| 19 | CustomerValues | `ms_holdings_customerValues` | Entity | 15,000,000 | — | 1.0 KB | +200K/mo | Per-customer computed values. |
| 20 | Instruments | `ms_holdings_instruments` | Entity (Reference) | 50,000 | — | 1.5 KB | +500/mo | Financial instruments catalog. |
| 21 | PortfolioAllocations | `ms_holdings_portfolioAllocations` | Entity | 2,000,000 | — | 0.8 KB | +50K/mo | Asset allocation per portfolio. |
| 22 | CustomerAllocations | `ms_holdings_customerAllocations` | Entity | 5,000,000 | — | 0.8 KB | +100K/mo | Customer-level allocation data. |
| 23 | EventAggregatorDetails | `ms_holdings_eventAggregatorDetails` | Entity | 5,000,000 | — | 1.0 KB | +500K/mo | Event aggregation for processing. |
| 24 | ArrangementEventProcessed | `ms_holdings_arrangementEventProcessed` | Entity | 20,000,000 | — | 0.8 KB | +2M/mo | Processed lifecycle events. Candidate for TTL. |
| 25 | SavingsPot | `ms_savingspot_savingspot` | Entity | 2,000,000 | — | 1.5 KB | +100K/mo | Sub-account savings pots. |
| 26 | SubAssetType | `ms_holdings_subAssetType` | Entity (Reference) | 200 | — | 0.3 KB | Rare | Asset sub-type reference. |
| 27 | PortfolioHoldings | `ms_holdings_portfolioHoldings` | Entity | 5,000,000 | — | 1.2 KB | +100K/mo | Securities/assets held in portfolios. |
| 28 | PortfolioValues | `ms_holdings_portfolioValues` | Entity | 2,000,000 | — | 1.5 KB | +50K/mo | Portfolio valuation. Has ScPosAssetValue embedded. |
| 29 | IddPrefixPhone | `ms_holdings_iddPrefixPhone` | Entity (Reference) | 500 | — | 0.2 KB | Rare | International dialing prefix reference. |
| 30 | PartyRoles | `ms_holdings_partyRoles` | Entity (Reference) | 100 | — | 0.3 KB | Rare | Role catalog (owner, signatory, etc.). |
| 31 | Status | `ms_holdings_status` | Entity (Reference) | 50 | — | 0.3 KB | Rare | Status catalog. |
| 32 | PaymentOrder | `ms_holdings_paymentOrder` | Entity | 20,000,000 | — | 2.0 KB | +1M/mo | Payment instructions. |
| 33 | PaymentTransaction | `ms_holdings_paymentTransaction` | Entity | 50,000,000 | — | 1.5 KB | +3M/mo | Payment execution records. |
| 34 | BankDates | `ms_holdings_bankDates` | Entity (Reference) | 1,000 | — | 0.3 KB | +365/yr | Business/settlement date calendar. |

**Summary:**
- **34 source tables** (including embeddable types counted under their parent)
- **4 tables with known volumes**: Arrangement (32.5M), PartyDetails (15M), Transaction (100M), Balance (100M)
- **Total estimated rows across all tables: ~665M**
- **Key relationships**: Arrangement is the root aggregate; PartyArrangement is the M:N junction between Arrangement and PartyDetails; Balance and Transaction are high-volume entities keyed by accountId

---

## 2. Access-Patterns Inventory

> Extracted from the API layer. TPS values are approximate peak figures. Priority assigned based on TPS and business criticality.

| # | API / Pattern Name | Priority | Complexity | Operation Type | Entities Accessed | Filter / Lookup Fields | Peak TPS | Latency Target | Read:Write |
|--:|-------------------|----------|-----------|---------------|-------------------|----------------------|--------:|---------------|-----------|
| AP-1 | **Get Balance by Account** `GET /holdings/accounts/{accountId}/balances` | P0 | Low | Query | Balance | `accountId` | 1,000 | P99 < 10ms | 100:1 |
| AP-2 | **Get Transactions by Account** `GET /holdings/accounts/{accountId}/transactions` | P0 | Low | Query | Transaction | `accountId`, processingDate, transactionAmount, narrative, categorisationId, businessKey (indexed) | 1,000 | P99 < 50ms | 100:1 |
| AP-3 | **Get Account Details** `GET /holdings/accounts/{accountId}` | P0 | High | Multi-Query Aggregate | Arrangement, PartyArrangement, PartyRoles, MsAltKey, BusinessContractActivity, BusinessContractBalance, AccountServices, LendingArr, DepositArr, AccountArr | `accountId`→`arrangementId` (via AltKey), then `arrangementId`, `partyId`, `(companyId,contractId,systemId)` | 500 | P99 < 50ms | 50:1 |
| AP-4 | **Get Arrangement by ID** `GET /holdings/arrangements/{arrangementId}` | P0 | Medium | Multi-Query Aggregate | Arrangement, PartyArrangement, PartyDetails, MsAltKey, LendingArr/DepositArr/AccountArr | `arrangementId`, `partyId` | 500 | P99 < 50ms | 50:1 |
| AP-5 | **Get Arrangements by Party** `GET /holdings/parties/{partyId}/arrangements` | P0 | High | Fan-out Query | PartyDetails, PartyArrangement, Arrangement, MsAltKey | `partyId`→`arrangementIds[]`→multi-arrangement fetch with filter `extensionData.customField` | 200 | P99 < 100ms | 50:1 |
| AP-6 | **Get Posting Restrictions** `GET /holdings/arrangements/{accountId}/postingRestrictions` | P1 | High | Multi-Query | Arrangement, PartyArrangement, PartyDetails, PostingRestrictDetails | `accountId`→`arrangementId`, `partyId`, `postingRestrictIds[]` | 100 | P99 < 50ms | 50:1 |
| AP-7 | **Get All Arrangements by Party** `GET /holdings/parties/{partyId}/arrangements/all` | P1 | Very High | Fan-out Aggregate | PartyDetails, PartyArrangement, Arrangement, MsAltKey, LendingArr/DepositArr/AccountArr | `partyId`→all linked arrangements (full payload) | 100 | P99 < 200ms | 50:1 |
| AP-8 | **Get Arrangement Schedules** `GET /holdings/arrangement/{arrangementId}/schedules` | P1 | Medium | Query | Arrangement, PaymentSchedules | `arrangementId` | 100 | P99 < 50ms | 50:1 |
| AP-9 | **Bulk Get Arrangements** `GET /holdings/bulkArrangements/{arrangementId}` | P1 | Very High | Multi-Query Aggregate | Multiple: Arrangement, MsAltKey, PaymentSchedules, PartyArrangement | `arrangementId` (multiple), alternateKey lookups | 100 | P99 < 200ms | 50:1 |

### Inferred Write Patterns (from application domain)

| # | Pattern Name | Priority | Operation | Entities | Est. Peak TPS | Notes |
|--:|-------------|----------|----------|----------|-------------:|-------|
| WP-1 | Create/Update Arrangement | P0 | Upsert | Arrangement + embeddables | 200 | Lifecycle: origination, amendments |
| WP-2 | Update Balance | P0 | Replace/Patch | Balance | 500 | After every transaction posting |
| WP-3 | Create Transaction | P0 | Create | Transaction | 500 | Financial transaction records |
| WP-4 | Update Party-Arrangement Link | P1 | Create/Delete | PartyArrangement | 100 | Add/remove parties from arrangements |
| WP-5 | Create Payment Order | P1 | Create | PaymentOrder, PaymentTransaction | 200 | Payment processing |
| WP-6 | Update Party Details | P1 | Replace/Patch | PartyDetails | 50 | KYC updates |
| WP-7 | Process Arrangement Event | P2 | Create | ArrangementEventProcessed | 200 | Lifecycle event logging |
| WP-8 | **Create Arrangement Bill** | P0 | Create | ArrangementBill | **500** | **⚠️ NEW — 1.53 bills/day/arrangement observed in sample. 32.5M arrangements × ~1.5/day = ~48.75M writes/day = ~564 TPS avg. Bills extracted from arrangement doc to avoid unbounded array growth toward 2 MB limit.** |

---

## 3. Cosmos DB Container Design

### Design Rationale

The relational schema has **34 tables** but only **3 primary access vectors** drive the API layer:

| Access Vector | APIs Served | Peak Combined TPS |
|:--|:--|--:|
| `accountId` (≈ arrangementId for accounts) | AP-1, AP-2, AP-3, AP-6 | 2,600 |
| `arrangementId` | AP-3, AP-4, AP-6, AP-8, AP-9 | 1,300 |
| `partyId` | AP-5, AP-7 | 300 |

**Key design decisions:**
1. **Arrangement as root aggregate** — 13 @Embeddable children embedded in arrangement document (excluding `arrangementBills` — see below). Co-locate PartyArrangement, contracts, schedules, and **bill documents** in same container by `arrangementId` to serve AP-3/AP-4 with single-partition queries.
   > ⚠️ **REVISED (Sample Document Analysis):** `arrangementBills` originally modeled as embedded array but real sample shows **472 bills (183.5 KB, 98.4% of doc)** growing at **1.53/day**. Extracted to separate `arrangementBill` documents in same container. Core arrangement doc is now ~3 KB. See Section 12 for full analysis.
2. **Party materialized view** — PartyArrangement must be queryable by both `arrangementId` AND `partyId`. Dual-write via Change Feed: primary copy in arrangements container (by arrangementId), materialized copy in parties container (by partyId).
3. **Balance and Transaction separated** — 100M rows each at 1,000 TPS. Different document sizes, different access patterns, different growth rates → dedicated containers with `accountId` as PK.
4. **Reference data consolidated** — All low-volume lookup tables (≤50K rows, rarely changing) in a single container with synthetic partition key.
5. **Alt-key lookup container** — Resolves `accountId`→`arrangementId`, `businessKey`→entityId for APIs that enter by non-PK field. Enables point-read-based resolution instead of cross-partition queries.

### 3.1 Container Mappings Summary

| # | Container | Partition Key | HPK Levels | Entity Types (`type` discriminator) | Est. Documents | Est. Storage | Throughput Mode |
|--:|-----------|:-------------|:-----------|:-------------------------------------|---------------:|-------------:|:----------------|
| 1 | **`holdings-arrangements`** | `/arrangementId` | — | `arrangement`, `partyArrangement`, `dueDiligence`, `paymentSchedule`, `contractActivity`, `contractBalance`, `postingRestrict`, `savingsPot`, **`arrangementBill`** ⚠️ NEW | **~6,675,500,000** ⚠️ | **~2,930 GB** ⚠️ | Autoscale |
| 2 | **`holdings-parties`** | `/partyId` | — | `partyDetails`, `partyArrangement` (materialized), `customerValues` | 95,000,000 | 200 GB | Autoscale |
| 3 | **`holdings-balances`** | `/accountId` | — | `balance` | 100,000,000 | 95 GB | Autoscale |
| 4 | **`holdings-transactions`** | `/accountId` | — | `transaction` | 100,000,000 | 143 GB | Autoscale |
| 5 | **`holdings-payments`** | `/accountId` | — | `paymentOrder`, `paymentTransaction` | 70,000,000 | 119 GB | Autoscale |
| 6 | **`holdings-portfolio`** | `/arrangementId` | — | `portfolioValues`, `portfolioHoldings`, `portfolioAllocation`, `customerAllocation`, `instrument` | 14,050,000 | 16 GB | Autoscale |
| 7 | **`holdings-events`** | `/arrangementId` | — | `eventProcessed`, `eventAggregator` | 25,000,000 | 22 GB | Autoscale (TTL: 90d) |
| 8 | **`holdings-reference`** | `/pk` (synthetic) | — | `countryCode`, `transactionType`, `subAssetType`, `partyRole`, `productGroupDetails`, `productDetails`, `iddPrefixPhone`, `officerDetails`, `companyDetails`, `status`, `bankDate` | 53,000 | 0.04 GB | Serverless |
| 9 | **`holdings-alt-keys`** | `/alternateKey` | — | `altKeyLookup` | 130,000,000 | 37 GB | Autoscale |

> **Total: 9 containers** — down from 34 RDBMS tables. Consolidation ratio ≈ 3.8:1.

### 3.2 JSON Document Models

#### Container 1: `holdings-arrangements` (PK: `/arrangementId`)

**Document type: `arrangement`** — Root aggregate with embedded children (**bills extracted** — see `arrangementBill` below)

> ⚠️ **REVISED:** Field names and structure updated to match real production sample (`SampleDocument_holdings.json`). `arrangementBills` array removed — now separate `arrangementBill` documents. Core arrangement doc is ~3 KB.

```json
{
  "id": "AA251130003V",
  "arrangementId": "AA251130003V",
  "type": "arrangement",
  "branch": "Blacktown",
  "country": "US",
  "currency": "USD",
  "startDate": "2025-04-23",
  "creationDate": "20250423",
  "processingDate": "20260226",
  "businessKey": "coretransact|US0010001|AA251130003V",
  "accountCategory": "1001",
  "productLine": "ACCOUNTS",
  "productGroup": "CURRENT",
  "isPortFolio": false,
  "isPortFolioAccount": false,
  "estmtEnabled": false,
  "externalIndicator": 0,
  "legalEntityId": "US0010001",
  "linkedReference": "74561987",
  "systemReference": "AA251130003V",
  "extArrangementId": "AA251130003V",
  "arrangementStatus": "CURRENT",

  "accountArrangement": {
    "extensionData": {},
    "processingDate": "20250423"
  },

  "companyDetails": {
    "mnemonic": "US0010001",
    "companyName": "Blacktown Corp"
  },

  "productDetails": {
    "productId": "CURRENT.ACCOUNT",
    "productDescription": "Everyday Current Account"
  },

  "arrangementInterest": [
    {
      "fixedRate": 0.0,
      "effectiveRate": 0.0,
      "dividentPaidYtd": 0.0,
      "intRateTierType": "LEVEL",
      "interestAccrued": 500.0,
      "interestProperty": "INTEREST",
      "lastPaidInterest": "20260224",
      "periodEndingDate": "20260224"
    }
  ],

  "scheduleDetails": [
    { "scheduleType": "FEE", "dueDate": "20260423" },
    { "scheduleType": "INTEREST", "dueDate": "20260224" },
    { "scheduleType": "STMTFR", "dueDate": "20260301" }
  ],

  "accountServices": [
    { "serviceType": "DEBIT_CARD", "status": "ACTIVE" },
    { "serviceType": "ONLINE_BANKING", "status": "ACTIVE" }
  ],

  "postingRestrictions": [],
  "extensionData": {},

  "_ts": 1740480000
}
```

> **Size: ~3.0 KB** (minified, without bills). Measured from real production sample.

**Document type: `arrangementBill`** — ⚠️ **NEW: Extracted from arrangement to prevent unbounded growth**

> Each billing event creates a separate document. At 1.53 bills/day this prevents the arrangement document from growing unbounded toward the 2 MB limit. Bills share the same `arrangementId` partition key for efficient single-partition queries.

```json
{
  "id": "BILL-AA251130003V-20260224-ACT.CHARGE-001",
  "arrangementId": "AA251130003V",
  "type": "arrangementBill",
  "billDate": "20260224",
  "billType": "ACT.CHARGE",
  "billAmount": 1.0,
  "billStatus": "SETTLED",
  "paymentMethod": "DUE",
  "deferDate": "20260224",
  "settlement": {
    "payinAccount": "74561987",
    "payinActivity": "ACHDEBITCR",
    "payoutAccount": "74561987",
    "payoutActivity": "ACHCREDITDR"
  },
  "billTypeDetails": {
    "propertyId": "CHARGE",
    "propertyAmount": 1.0,
    "propertyNarrative": "Monthly Account Fee"
  },
  "_ts": 1740480000
}
```

> **Size: ~0.4 KB** per bill. Indexed by `billDate` for chronological queries. See Section 12 for growth projections.
```

**Document type: `partyArrangement`** — Party-to-arrangement link (co-located by arrangementId), **with denormalized `partySummary`**

> ⚠️ **REVISED:** Added `partySummary` object to eliminate the cross-container hop to `holdings-parties` for AP-3, AP-4, and AP-6. Party detail changes are rare (~50 TPS) and propagated via Change Feed (CP-5). Trade-off: ~0.2 KB extra per doc, ~700 RU/s additional write cost — saves 2 RU × 1,100 TPS reads = 2,200 RU/s.

```json
{
  "id": "PA-ARR-2026-00012345-PTY-001",
  "arrangementId": "ARR-2026-00012345",
  "type": "partyArrangement",
  "partyId": "PTY-001",
  "partyRole": "OWNER",
  "isPartyOwner": true,
  "isActive": true,
  "partySummary": {
    "partyName": "John Doe",
    "firstName": "John",
    "lastName": "Doe",
    "customerSegment": "RETAIL",
    "nationality": "GB"
  },
  "_ts": 1740480000
}
```

**Document type: `dueDiligence`**

```json
{
  "id": "DD-ARR-2026-00012345",
  "arrangementId": "ARR-2026-00012345",
  "type": "dueDiligence",
  "riskClassification": "LOW",
  "lastReviewDate": "2025-12-01",
  "nextReviewDate": "2026-12-01",
  "transactionalIntensions": {
    "expectedMonthlyCredits": 5000.00,
    "expectedMonthlyDebits": 4500.00,
    "expectedTransactionVolume": 50,
    "purposeOfAccount": "SALARY_AND_EXPENSES"
  },
  "_ts": 1740480000
}
```

**Document type: `paymentSchedule`**

```json
{
  "id": "PS-ARR-2026-00012345-001",
  "arrangementId": "ARR-2026-00012345",
  "type": "paymentSchedule",
  "scheduleType": "REPAYMENT",
  "scheduledDate": "2026-03-15",
  "amount": 450.00,
  "currency": "GBP",
  "status": "PENDING",
  "principalComponent": 400.00,
  "interestComponent": 50.00,
  "_ts": 1740480000
}
```

**Document type: `contractActivity`**

```json
{
  "id": "CA-GB0010001-ARR-2026-00012345-001",
  "arrangementId": "ARR-2026-00012345",
  "type": "contractActivity",
  "activityId": "ACT-20260215-001",
  "systemId": "T24",
  "companyId": "GB0010001",
  "contractId": "ARR-2026-00012345",
  "businessKey": "BK-00012345",
  "activityType": "ACCOUNT_OPENING",
  "activityDate": "2024-03-15",
  "contextDetails": {
    "channel": "BRANCH",
    "userId": "USR-3001",
    "remarks": "New current account opened"
  },
  "_ts": 1740480000
}
```

**Document type: `contractBalance`**

```json
{
  "id": "CB-GB0010001-ARR-2026-00012345",
  "arrangementId": "ARR-2026-00012345",
  "type": "contractBalance",
  "entityId": "CB-00012345",
  "systemId": "T24",
  "companyId": "GB0010001",
  "contractId": "ARR-2026-00012345",
  "businessKey": "BK-00012345",
  "balanceType": "WORKING",
  "amount": 12500.50,
  "currency": "GBP",
  "asOfDate": "2026-02-25",
  "_ts": 1740480000
}
```

**Document type: `postingRestrict`**

```json
{
  "id": "PRS-ARR-2026-00012345-PR-001",
  "arrangementId": "ARR-2026-00012345",
  "type": "postingRestrict",
  "restrictionId": "PR-001",
  "restrictionType": "CREDIT",
  "transactionCode": "AC",
  "startDate": "2026-01-01",
  "endDate": null,
  "reason": "COMPLIANCE_HOLD",
  "active": true,
  "_ts": 1740480000
}
```

**Document type: `savingsPot`**

```json
{
  "id": "SP-ARR-2026-00012345-POT01",
  "arrangementId": "ARR-2026-00012345",
  "type": "savingsPot",
  "potName": "Holiday Fund",
  "targetAmount": 2000.00,
  "currentAmount": 850.00,
  "currency": "GBP",
  "createdDate": "2025-06-01",
  "_ts": 1740480000
}
```

---

#### Container 2: `holdings-parties` (PK: `/partyId`)

**Document type: `partyDetails`** — Party master with embedded restriction details and alt keys

```json
{
  "id": "PTY-001",
  "partyId": "PTY-001",
  "type": "partyDetails",
  "businessKey": "BK-PTY-001",
  "firstName": "John",
  "lastName": "Doe",
  "dateOfBirth": "1985-07-22",
  "nationality": "GB",
  "customerSegment": "RETAIL",
  "alternateKeys": [
    { "keyType": "CIF_NUMBER", "keyValue": "CIF-90001234" },
    { "keyType": "TAX_ID", "keyValue": "AB123456C" }
  ],
  "postingRestrictDetails": [
    {
      "restrictionId": "PRS-PTY-001",
      "restrictionType": "SANCTIONS_CHECK",
      "active": false,
      "lastChecked": "2025-11-15"
    }
  ],
  "_ts": 1740480000
}
```

**Document type: `partyArrangement`** — Materialized view (populated by Change Feed from `holdings-arrangements`)

```json
{
  "id": "PA-PTY-001-ARR-2026-00012345",
  "partyId": "PTY-001",
  "type": "partyArrangement",
  "arrangementId": "ARR-2026-00012345",
  "partyRole": "OWNER",
  "isPartyOwner": true,
  "isActive": true,
  "arrangementSummary": {
    "productGroup": "ACCOUNTS",
    "productId": "CURRENT_ACCOUNT",
    "productLine": "LENDING",
    "status": "CURRENT",
    "arrangementStatus": "CURRENT",
    "currency": "GBP",
    "linkedReference": "AC-10029384756",
    "accountCategory": "CURRENT",
    "startDate": "2024-04-18",
    "extArrangementId": "EXT-ARR-001"
  },
  "_ts": 1740480000
}
```

> **⚠️ ENRICHED (Round 3):** `arrangementSummary` expanded with `productLine`, `arrangementStatus`, `linkedReference`, `accountCategory`, `startDate`, `extArrangementId`. This lets AP-5 list-mode serve entirely from `holdings-parties` (single container, 5 RU) without fanning out to `holdings-arrangements`. CP-2 Change Feed propagates these fields on arrangement updates.

**Document type: `customerValues`**

```json
{
  "id": "CV-PTY-001",
  "partyId": "PTY-001",
  "type": "customerValues",
  "totalRelationshipValue": 125000.00,
  "profitabilityScore": 78.5,
  "riskScore": "LOW",
  "lastCalculated": "2026-02-24",
  "_ts": 1740480000
}
```

---

#### Container 3: `holdings-balances` (PK: `/accountId`)

**Document type: `balance`**

```json
{
  "id": "BAL-AC-10029384756-WORKING",
  "accountId": "AC-10029384756",
  "type": "balance",
  "businessKey": "BK-00012345",
  "balanceType": "WORKING",
  "amount": 12500.50,
  "currency": "GBP",
  "lockedAmounts": [
    {
      "lockId": "LCK-001",
      "amount": 500.00,
      "reason": "CARD_AUTHORIZATION",
      "expiryDate": "2026-02-26"
    }
  ],
  "availableBalance": 12000.50,
  "lastUpdated": "2026-02-25T14:30:00Z",
  "_ts": 1740480000
}
```

---

#### Container 4: `holdings-transactions` (PK: `/accountId`)

**Document type: `transaction`**

```json
{
  "id": "TXN-AC-10029384756-20260225-001",
  "accountId": "AC-10029384756",
  "type": "transaction",
  "businessKey": "BK-TXN-20260225-001",
  "processingDate": "2026-02-25",
  "valueDate": "2026-02-25",
  "bookingDate": "2026-02-25",
  "transactionType": "CREDIT",
  "transactionCode": "FT",
  "transactionAmount": 2500.00,
  "currency": "GBP",
  "narrative": "SALARY PAYMENT - ACME CORP",
  "categorisationId": "CAT-INCOME-SALARY",
  "runningBalance": 12500.50,
  "counterparty": {
    "name": "ACME CORPORATION",
    "accountId": "AC-COUNTERPARTY-999"
  },
  "_ts": 1740480000
}
```

---

#### Container 5: `holdings-payments` (PK: `/accountId`)

**Document type: `paymentOrder`**

```json
{
  "id": "PO-AC-10029384756-20260225-001",
  "accountId": "AC-10029384756",
  "type": "paymentOrder",
  "paymentOrderId": "PO-20260225-001",
  "orderType": "SEPA_CREDIT_TRANSFER",
  "status": "COMPLETED",
  "amount": 150.00,
  "currency": "GBP",
  "debitAccount": "AC-10029384756",
  "creditAccount": "AC-99887766554",
  "beneficiaryName": "Jane Smith",
  "reference": "Invoice INV-2026-0042",
  "createdDate": "2026-02-25T10:00:00Z",
  "executionDate": "2026-02-25",
  "_ts": 1740480000
}
```

**Document type: `paymentTransaction`**

```json
{
  "id": "PT-AC-10029384756-20260225-001",
  "accountId": "AC-10029384756",
  "type": "paymentTransaction",
  "paymentOrderId": "PO-20260225-001",
  "transactionRef": "TXN-AC-10029384756-20260225-002",
  "status": "SETTLED",
  "amount": 150.00,
  "currency": "GBP",
  "settlementDate": "2026-02-25",
  "clearingSystem": "FASTER_PAYMENTS",
  "_ts": 1740480000
}
```

---

#### Container 6: `holdings-portfolio` (PK: `/arrangementId`)

**Document type: `portfolioValues`**

```json
{
  "id": "PV-ARR-PORT-00001",
  "arrangementId": "ARR-PORT-00001",
  "type": "portfolioValues",
  "totalValue": 250000.00,
  "currency": "GBP",
  "valuationDate": "2026-02-25",
  "scPosAssetValues": [
    { "assetType": "EQUITY", "value": 150000.00, "percentage": 60.0 },
    { "assetType": "BONDS", "value": 75000.00, "percentage": 30.0 },
    { "assetType": "CASH", "value": 25000.00, "percentage": 10.0 }
  ],
  "_ts": 1740480000
}
```

**Document type: `portfolioHoldings`**

```json
{
  "id": "PH-ARR-PORT-00001-INST-001",
  "arrangementId": "ARR-PORT-00001",
  "type": "portfolioHoldings",
  "instrumentId": "INST-AAPL",
  "instrumentName": "Apple Inc.",
  "quantity": 100,
  "averageCost": 150.00,
  "currentPrice": 185.50,
  "marketValue": 18550.00,
  "currency": "USD",
  "unrealizedPnL": 3550.00,
  "_ts": 1740480000
}
```

**Document type: `portfolioAllocation`**

```json
{
  "id": "PALL-ARR-PORT-00001-EQ",
  "arrangementId": "ARR-PORT-00001",
  "type": "portfolioAllocation",
  "assetClass": "EQUITY",
  "targetAllocation": 60.0,
  "actualAllocation": 60.0,
  "rebalanceRequired": false,
  "_ts": 1740480000
}
```

**Document type: `customerAllocation`**

```json
{
  "id": "CALL-ARR-PORT-00001-PTY-001",
  "arrangementId": "ARR-PORT-00001",
  "type": "customerAllocation",
  "partyId": "PTY-001",
  "allocationPercentage": 100.0,
  "investmentProfile": "BALANCED",
  "_ts": 1740480000
}
```

**Document type: `instrument`**

```json
{
  "id": "INST-AAPL",
  "arrangementId": "GLOBAL",
  "type": "instrument",
  "instrumentCode": "AAPL",
  "instrumentName": "Apple Inc.",
  "instrumentType": "EQUITY",
  "exchange": "NASDAQ",
  "currency": "USD",
  "isin": "US0378331005",
  "_ts": 1740480000
}
```

---

#### Container 7: `holdings-events` (PK: `/arrangementId`, TTL: 90 days)

**Document type: `eventProcessed`**

```json
{
  "id": "EVT-ARR-2026-00012345-20260225-001",
  "arrangementId": "ARR-2026-00012345",
  "type": "eventProcessed",
  "eventType": "INTEREST_ACCRUAL",
  "eventDate": "2026-02-25",
  "processedAt": "2026-02-25T02:00:00Z",
  "status": "COMPLETED",
  "details": {
    "interestAmount": 1.72,
    "accrualPeriod": "2026-02-25"
  },
  "ttl": 7776000,
  "_ts": 1740480000
}
```

**Document type: `eventAggregator`**

```json
{
  "id": "EA-ARR-2026-00012345-202602",
  "arrangementId": "ARR-2026-00012345",
  "type": "eventAggregator",
  "aggregationPeriod": "2026-02",
  "eventCounts": {
    "INTEREST_ACCRUAL": 25,
    "FEE_CHARGE": 1,
    "STATEMENT_GENERATION": 1
  },
  "totalEvents": 27,
  "ttl": 7776000,
  "_ts": 1740480000
}
```

---

#### Container 8: `holdings-reference` (PK: `/pk`, synthetic)

**Document type: `countryCode`**

```json
{
  "id": "REF-COUNTRY-GB",
  "pk": "COUNTRY#GB",
  "type": "countryCode",
  "code": "GB",
  "name": "United Kingdom",
  "isoAlpha3": "GBR",
  "numericCode": "826",
  "countrySubDivisions": [
    { "code": "GB-ENG", "name": "England" },
    { "code": "GB-SCT", "name": "Scotland" },
    { "code": "GB-WLS", "name": "Wales" },
    { "code": "GB-NIR", "name": "Northern Ireland" }
  ]
}
```

**Document type: `transactionType`**

```json
{
  "id": "REF-TXNTYPE-FT",
  "pk": "TRANSACTION_TYPE#FT",
  "type": "transactionType",
  "code": "FT",
  "description": "Funds Transfer",
  "category": "PAYMENT",
  "isCredit": true,
  "isDebit": true
}
```

**Document type: `partyRole`**

```json
{
  "id": "REF-ROLE-OWNER",
  "pk": "PARTY_ROLE#OWNER",
  "type": "partyRole",
  "roleCode": "OWNER",
  "roleName": "Account Owner",
  "description": "Primary owner of the arrangement"
}
```

**Document type: `productDetails`**

```json
{
  "id": "REF-PROD-CURRENT_ACCOUNT",
  "pk": "PRODUCT#CURRENT_ACCOUNT",
  "type": "productDetails",
  "productId": "CURRENT_ACCOUNT",
  "productName": "Everyday Current Account",
  "productGroup": "ACCOUNTS",
  "retentionPeriod": { "period": 7, "unit": "YEARS" },
  "features": ["CHEQUEBOOK", "DEBIT_CARD", "ONLINE_BANKING"]
}
```

**Document type: `productGroupDetails`**

```json
{
  "id": "REF-PG-ACCOUNTS",
  "pk": "PRODUCT_GROUP#ACCOUNTS",
  "type": "productGroupDetails",
  "productGroupIdentifier": "ACCOUNTS",
  "groupName": "Current & Savings Accounts",
  "retentionPeriod": { "period": 7, "unit": "YEARS" }
}
```

**Document type: `status`**

```json
{
  "id": "REF-STATUS-CURRENT",
  "pk": "STATUS#CURRENT",
  "type": "status",
  "code": "CURRENT",
  "description": "Active and in good standing"
}
```

**Document type: `bankDate`**

```json
{
  "id": "REF-BANKDATE-20260225",
  "pk": "BANK_DATE#20260225",
  "type": "bankDate",
  "date": "2026-02-25",
  "isBusinessDay": true,
  "isSettlementDay": true,
  "region": "GB"
}
```

**Document type: `iddPrefixPhone`**

```json
{
  "id": "REF-IDD-GB",
  "pk": "IDD_PREFIX#GB",
  "type": "iddPrefixPhone",
  "countryCode": "GB",
  "prefix": "+44",
  "description": "United Kingdom"
}
```

**Document type: `subAssetType`**

```json
{
  "id": "REF-SUBASSET-EQUITY-LARGE",
  "pk": "SUB_ASSET#EQUITY-LARGE",
  "type": "subAssetType",
  "code": "EQUITY-LARGE",
  "parentAssetType": "EQUITY",
  "description": "Large Cap Equities"
}
```

**Document type: `officerDetails`**

```json
{
  "id": "REF-OFFICER-OFF-5001",
  "pk": "OFFICER#OFF-5001",
  "type": "officerDetails",
  "officerId": "OFF-5001",
  "officerName": "Sarah Manager",
  "role": "RELATIONSHIP_MANAGER",
  "branch": "LONDON-01"
}
```

**Document type: `companyDetails`**

```json
{
  "id": "REF-COMPANY-GB0010001",
  "pk": "COMPANY#GB0010001",
  "type": "companyDetails",
  "companyId": "GB0010001",
  "companyName": "Temenos UK Ltd",
  "region": "GB",
  "branchCode": "LONDON-01"
}
```

---

#### Container 9: `holdings-alt-keys` (PK: `/alternateKey`)

**Document type: `altKeyLookup`**

```json
{
  "id": "ALT-ACCOUNT_ID-AC-10029384756",
  "alternateKey": "AC-10029384756",
  "type": "altKeyLookup",
  "keyType": "ACCOUNT_ID",
  "entityType": "ARRANGEMENT",
  "entityId": "ARR-2026-00012345",
  "entityName": "Everyday Current Account"
}
```

```json
{
  "id": "ALT-IBAN-GB29NWBK60161331926819",
  "alternateKey": "GB29NWBK60161331926819",
  "type": "altKeyLookup",
  "keyType": "IBAN",
  "entityType": "ARRANGEMENT",
  "entityId": "ARR-2026-00012345",
  "entityName": "Everyday Current Account"
}
```

```json
{
  "id": "ALT-CIF-CIF-90001234",
  "alternateKey": "CIF-90001234",
  "type": "altKeyLookup",
  "keyType": "CIF_NUMBER",
  "entityType": "PARTY",
  "entityId": "PTY-001",
  "entityName": "John Doe"
}
```

---

### 3.3 Indexing Policies

#### `holdings-arrangements`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/status/?" },
    { "path": "/arrangementStatus/?" },
    { "path": "/productGroup/?" },
    { "path": "/productLine/?" },
    { "path": "/linkedReference/?" },
    { "path": "/businessKey/?" },
    { "path": "/partyId/?" },
    { "path": "/partyRole/?" },
    { "path": "/isActive/?" },
    { "path": "/contractId/?" },
    { "path": "/systemId/?" },
    { "path": "/companyId/?" },
    { "path": "/scheduledDate/?" },
    { "path": "/scheduleType/?" },
    { "path": "/billDate/?" },
    { "path": "/billType/?" }
  ],
  "excludedPaths": [
    { "path": "/emailAddresses/*" },
    { "path": "/correspondenceAddresses/*" },
    { "path": "/alternateKeys/*" },
    { "path": "/contextDetails/*" },
    { "path": "/transactionalIntensions/*" },
    { "path": "/settlement/*" },
    { "path": "/billTypeDetails/*" },
    { "path": "/extensionData/*" },
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/arrangementStatus", "order": "ascending" }
    ],
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/systemId", "order": "ascending" },
      { "path": "/companyId", "order": "ascending" }
    ],
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/scheduledDate", "order": "ascending" }
    ],
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/billDate", "order": "descending" }
    ],
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/billType", "order": "ascending" },
      { "path": "/billDate", "order": "descending" }
    ]
  ]
}
```

> ⚠️ **REVISED:** Added `billDate`, `billType` indexed paths and composite indexes for efficient bill queries (`type = 'arrangementBill' AND billDate > @since ORDER BY billDate DESC`). Removed `/arrangementBills/*` exclusion (no longer embedded). Added `/settlement/*`, `/billTypeDetails/*`, `/extensionData/*` exclusions. Updated field names to match real production fields (`arrangementStatus` instead of `status`, `linkedReference` instead of `linkedAccountId`, `productLine` instead of `productId`).
```

#### `holdings-parties`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/businessKey/?" },
    { "path": "/arrangementId/?" },
    { "path": "/isActive/?" },
    { "path": "/partyRole/?" },
    { "path": "/customerSegment/?" },
    { "path": "/arrangementSummary/productGroup/?" },
    { "path": "/arrangementSummary/status/?" }
  ],
  "excludedPaths": [
    { "path": "/postingRestrictDetails/*" },
    { "path": "/alternateKeys/*" },
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/isActive", "order": "ascending" }
    ],
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/arrangementSummary/status", "order": "ascending" }
    ]
  ]
}
```

#### `holdings-balances`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/balanceType/?" },
    { "path": "/businessKey/?" },
    { "path": "/lastUpdated/?" }
  ],
  "excludedPaths": [
    { "path": "/lockedAmounts/*" },
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/balanceType", "order": "ascending" },
      { "path": "/lastUpdated", "order": "descending" }
    ]
  ]
}
```

#### `holdings-transactions`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/processingDate/?" },
    { "path": "/transactionAmount/?" },
    { "path": "/narrative/?" },
    { "path": "/categorisationId/?" },
    { "path": "/businessKey/?" },
    { "path": "/transactionType/?" },
    { "path": "/bookingDate/?" }
  ],
  "excludedPaths": [
    { "path": "/counterparty/*" },
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/processingDate", "order": "descending" },
      { "path": "/transactionAmount", "order": "descending" }
    ],
    [
      { "path": "/categorisationId", "order": "ascending" },
      { "path": "/processingDate", "order": "descending" }
    ],
    [
      { "path": "/transactionType", "order": "ascending" },
      { "path": "/bookingDate", "order": "descending" }
    ]
  ]
}
```

#### `holdings-payments`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/paymentOrderId/?" },
    { "path": "/status/?" },
    { "path": "/createdDate/?" },
    { "path": "/executionDate/?" },
    { "path": "/orderType/?" }
  ],
  "excludedPaths": [
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/status", "order": "ascending" },
      { "path": "/executionDate", "order": "descending" }
    ]
  ]
}
```

#### `holdings-portfolio`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/instrumentId/?" },
    { "path": "/assetClass/?" },
    { "path": "/valuationDate/?" },
    { "path": "/instrumentType/?" }
  ],
  "excludedPaths": [
    { "path": "/scPosAssetValues/*" },
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/valuationDate", "order": "descending" }
    ]
  ]
}
```

#### `holdings-events`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/eventType/?" },
    { "path": "/eventDate/?" },
    { "path": "/status/?" },
    { "path": "/aggregationPeriod/?" }
  ],
  "excludedPaths": [
    { "path": "/details/*" },
    { "path": "/eventCounts/*" },
    { "path": "/*" }
  ],
  "compositeIndexes": [
    [
      { "path": "/type", "order": "ascending" },
      { "path": "/eventDate", "order": "descending" }
    ]
  ]
}
```

#### `holdings-reference`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/*" }
  ],
  "excludedPaths": []
}
```
> Reference data is small (< 1 MB total). Full indexing is appropriate; no optimization needed.

#### `holdings-alt-keys`

```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    { "path": "/type/?" },
    { "path": "/keyType/?" },
    { "path": "/entityType/?" },
    { "path": "/entityId/?" }
  ],
  "excludedPaths": [
    { "path": "/*" }
  ]
}
```

---

## 4. Access-Pattern → SDK-Call Mapping

> **SDK:** Azure Cosmos DB Java SDK v4 (`com.azure:azure-cosmos`) with **Spring Boot 3.x** and **Spring Data Azure Cosmos DB** (`azure-spring-data-cosmos`). All examples use reactive (`CosmosAsyncContainer`) or Spring Data repository patterns. For blocking (synchronous) calls, use `CosmosContainer` equivalents or `block()` on reactive chains.

### 4.1 Original Access Patterns

| # | Pattern | RDBMS SQL / Sequence | Cosmos DB Container(s) | PK Hit | Cosmos DB Java SDK Call(s) | RU/op (est.) | Peak TPS | Peak RU/s |
|--:|---------|---------------------|----------------------|--------|----------------------|--------:|--------:|---------:|
| AP-1 | **Get Balance** | `SELECT * FROM balance WHERE accountId = @acctId` | `holdings-balances` | Single ✅ | `balancesContainer.queryItems("SELECT * FROM c WHERE c.accountId = @acctId", new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(acctId)), Balance.class)` — single-partition query by PK | 3 | 1,000 | 3,000 |
| AP-2 | **Get Transactions** | `SELECT * FROM transaction WHERE accountId = @acctId AND <filters> ORDER BY processingDate DESC` | `holdings-transactions` | Single ✅ | `transactionsContainer.queryItems(new SqlQuerySpec("SELECT * FROM c WHERE c.accountId = @acctId AND c.processingDate >= @from ORDER BY c.processingDate DESC", params), new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(acctId)), Transaction.class)` — paginated single-partition query with composite index. Use `CosmosPagedIterable` (sync) or `CosmosPagedFlux` (reactive) for pagination. | 10 | 1,000 | 10,000 |
| AP-3 | **Get Account Details** | Multi-JOIN: arrangement ← partyArrangement ← altKey, contractActivity, contractBalance (6+ queries) | `holdings-alt-keys` → `holdings-arrangements` | Single ✅ (per container) | **Step 1:** `altKeysContainer.readItem(acctId, new PartitionKey(acctId), AltKey.class)` → resolve `arrangementId` (1 RU). **Step 2:** `arrangementsContainer.queryItems("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type != 'arrangementBill'", new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(arrId)), JsonNode.class)` → arrangement + partyArrangement (with denormalized `partySummary`) + related docs (5 RU). **⚠️ REVISED: Party hop eliminated — `partySummary` in partyArrangement doc provides partyName, customerSegment, nationality. No step 3 needed.** **Step 3:** PartyRoles from cached reference data (`@Cacheable`, 0 RU) | 6 | 500 | 3,000 |
| AP-4 | **Get Arrangement** | Multi-JOIN: arrangement ← partyArrangement (with partySummary) | `holdings-arrangements` ✅ **SINGLE CONTAINER** | Single ✅ | `arrangementsContainer.queryItems("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type != 'arrangementBill'", new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(arrId)), JsonNode.class)` → returns arrangement doc + partyArrangement docs with embedded `partySummary`. **⚠️ REVISED: Now single-container, single-partition, single round-trip.** Party details (name, segment, nationality) denormalized in `partyArrangement.partySummary`. No cross-container hop. | 5 | 500 | 2,500 |
| AP-5 | **Get Party Arrangements** | `party → partyArrangement → arrangement → altKey` (multi-phase fan-out, 10+ queries) | `holdings-parties` → `holdings-arrangements` | Single ✅ (initial) → Multi-read fan-out ⚠️ | **List mode ✅ (OPTIMIZED):** `partiesContainer.queryItems("SELECT * FROM c WHERE c.partyId = @pId AND c.type = 'partyArrangement'", new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(pId)), PartyArrangement.class)` (5 RU) — enriched `arrangementSummary` contains productGroup, productLine, status, currency, linkedReference, accountCategory, startDate, extArrangementId. **Single container, no fan-out.** **Full-detail mode:** Step 1 (5 RU) + Step 2: For each arrangementId (avg 3): `arrangementsContainer.readItem(arrId, new PartitionKey(arrId), Arrangement.class)` ×3 (3 RU), parallelized via `Flux.merge()` = 8 RU total. | **5** (list) / 8 (full) | 200 | **1,000** (list) / 1,600 (full) |
| AP-6 | **Get Posting Restrictions** | `arrangement ← partyArrangement ← partyDetails ← postingRestrictDetails` (4+ queries) | `holdings-alt-keys` → `holdings-arrangements` → `holdings-parties` | Single ✅ (per container) | **Step 1:** `altKeysContainer.readItem(acctId, new PartitionKey(acctId), AltKey.class)` → resolve (1 RU). **Step 2:** `arrangementsContainer.queryItems("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type IN ('arrangement','postingRestrict','partyArrangement')", opts, JsonNode.class)` — arrangement + postingRestrict + partyArrangement with `partySummary` (5 RU). **Step 3:** `partiesContainer.readItem(partyId, new PartitionKey(partyId), PartyDetails.class)` — party posting restrictions (2 RU). ⚠️ restriction details NOT denormalized (too volatile). | 8 | 100 | 800 |
| AP-7 | **Get All Party Arrangements** | Same as AP-5 but full payload | `holdings-parties` → `holdings-arrangements` | Single ✅ → Fan-out ⚠️ | Same as AP-5 full-detail mode but avg 5 arrangements per party. Step 2 parallelized via `Flux.merge(readItem calls)`. | 20 | 100 | 2,000 |
| AP-8 | **Get Schedules** | `arrangement + paymentSchedules` (2 queries) | `holdings-arrangements` | Single ✅ | `arrangementsContainer.queryItems("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type IN ('arrangement','paymentSchedule')", new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(arrId)), JsonNode.class)` | 8 | 100 | 800 |
| AP-9 | **Bulk Arrangements** | `altKey → arrangement → schedules → partyArrangement → altKey` per ID (5+ queries × N) | `holdings-arrangements` ✅ **SINGLE CONTAINER** | Single ✅ (per ID) | **⚠️ REVISED:** For each arrangementId in batch (avg 5): `arrangementsContainer.queryItems("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type != 'arrangementBill'", opts, JsonNode.class)` (**5 RU** × 5 = **25 RU** total). Parallelized via `Flux.merge()`. **Corrections:** (a) Alt-key hop removed — API input is `arrangementId`, goes direct-to-partition. (b) RU per arrangement reduced from 10 to 5 (doc ~3 KB after bill extraction). | **25** | 100 | **2,500** |
| AP-10 | **⚠️ NEW: Get Bills by Arrangement** | `SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type = 'arrangementBill' ORDER BY c.billDate DESC` | `holdings-arrangements` | Single ✅ | `arrangementsContainer.queryItems(new SqlQuerySpec("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type = 'arrangementBill' ORDER BY c.billDate DESC", params), new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(arrId)), ArrangementBill.class)` — paginated single-partition query via `CosmosPagedFlux`. Uses composite index `(type ASC, billDate DESC)`. | 8 | 200 | 1,600 |

### 4.2 Compensating (NEW) Access Patterns

These are **new patterns introduced by the Cosmos DB design** to maintain denormalized data and materialized views.

| # | Pattern | Origin | Type | Container | PK Hit | Cosmos DB Java SDK Call | RU/op | Est. TPS | Peak RU/s |
|--:|---------|--------|------|-----------|--------|-------------------|------:|--------:|---------:|
| CP-1 | **Sync PartyArrangement to Parties** | ⚠️ NEW — materialized view | Change Feed → Upsert | `holdings-parties` | Single ✅ | Change Feed processor on `holdings-arrangements` (via `ChangeFeedProcessor.changeFeedProcessorBuilder()`) → filter `type = 'partyArrangement'` → `partiesContainer.upsertItem(doc, new PartitionKey(partyId), new CosmosItemRequestOptions())` | 7 | 100 | 700 |
| CP-2 | **Sync Arrangement Summary to Party View** | ⚠️ NEW — denormalization | Change Feed → Patch | `holdings-parties` | Single ✅ | Change Feed on `holdings-arrangements` → filter `type = 'arrangement'` → for each linked partyArrangement: `partiesContainer.patchItem(docId, new PartitionKey(partyId), CosmosPatchOperations.create().replace("/arrangementSummary", enrichedSummary), PartyArrangement.class)` to update `arrangementSummary` | 11 | 50 | 550 |
| CP-3 | **Sync AltKey Lookup** | ⚠️ NEW — lookup maintenance | Change Feed → Upsert | `holdings-alt-keys` | Single ✅ | Change Feed on `holdings-arrangements` → extract `alternateKeys[]` → `altKeysContainer.upsertItem(altKeyDoc, new PartitionKey(alternateKey), new CosmosItemRequestOptions())` | 5 | 100 | 500 |
| CP-4 | **Balance Update → Write-through** | ⚠️ NEW — eventual (if dual-path) | Direct write | `holdings-balances` | Single ✅ | After transaction posting: `balancesContainer.replaceItem(balance, balance.getId(), new PartitionKey(accountId), new CosmosItemRequestOptions())` | 8 | 500 | 4,000 |
| CP-5 | **Sync Party Details → partyArrangement.partySummary** | ⚠️ NEW — denormalization for AP-3/AP-4 | Change Feed → Patch | `holdings-arrangements` | Single ✅ | Change Feed on `holdings-parties` → filter `type = 'partyDetails'` → for each linked arrangement: `arrangementsContainer.patchItem(docId, new PartitionKey(arrId), CosmosPatchOperations.create().replace("/partySummary", partySummaryMap), PartyArrangement.class)` | 7 | 100 | 700 |

**Origin Legend:**
- **Original** — Direct migration of existing RDBMS access pattern
- **⚠️ NEW — materialized view** — Compensating write to maintain party-centric view
- **⚠️ NEW — denormalization** — Propagating arrangement summary to party view
- **⚠️ NEW — lookup maintenance** — Maintaining alternate key resolution container

---

## 5. Partition Key Validation

### 5.1 PK Choice Rationale per Container

| Container | PK | Cardinality | Access Pattern Coverage | Hot Partition Risk |
|-----------|-----|-------------|------------------------|-------------------|
| `holdings-arrangements` | `/arrangementId` | 32.5M distinct values ✅ | AP-3, AP-4, AP-5 (full-detail step 2), AP-6, AP-7, AP-8, **AP-9** ✅ R3 — all single-partition ✅ | Even distribution. Peak per partition: ~0.3 RU (32.5M keys, ~17.8K total RU/s ⚠️ R3 → 0.0005 RU/key avg). No hot key. ✅ |
| `holdings-parties` | `/partyId` | 15M distinct values ✅ | **AP-5 (list-mode ✅ R3)**, AP-5 (full-detail step 1), AP-7 (step 1), CP-1, CP-2 — all single-partition ✅ | Even. 15M keys. ✅ |
| `holdings-balances` | `/accountId` | ~25M distinct values ✅ | AP-1 — single-partition ✅ | Even. 1,000 TPS / 25M keys. ✅ |
| `holdings-transactions` | `/accountId` | ~25M distinct values ✅ | AP-2 — single-partition ✅ | Avg ~4 txns per account per query. ✅ |
| `holdings-payments` | `/accountId` | ~25M distinct values ✅ | Payment queries by account — single-partition ✅ | Even. ✅ |
| `holdings-portfolio` | `/arrangementId` | ~500K distinct values ✅ | Portfolio-specific queries ✅ | Even. Low TPS. ✅ |
| `holdings-events` | `/arrangementId` | 32.5M distinct values ✅ | Event queries by arrangement ✅ | Even. TTL manages volume. ✅ |
| `holdings-reference` | `/pk` (synthetic) | ~3K distinct values | Point reads by synthetic key ✅ | Low volume, no concern. ✅ |
| `holdings-alt-keys` | `/alternateKey` | ~130M distinct values ✅ | AP-3 (step 1), AP-6 (step 1) — point reads ✅. ~~AP-9~~ removed R3 (direct-to-arrangements). | Even. ✅ |

### 5.2 Data Volume per Logical Partition

| Container | PK | Docs per Partition (avg) | Data per Partition (avg) | Max Expected | < 20 GB? |
|-----------|-----|------------------------:|-------------------------:|-------------:|---------:|
| `holdings-arrangements` | `/arrangementId` | **~205** ⚠️ | **~85 KB** ⚠️ | **~500 KB** (active arrangement with ~1,000 bills × 0.4 KB + core docs) | ✅ |
| `holdings-parties` | `/partyId` | 6.3 | 14 KB | ~200 KB (party with 50+ arrangements) | ✅ |
| `holdings-balances` | `/accountId` | 4 | 4 KB | ~40 KB (account with many balance types) | ✅ |
| `holdings-transactions` | `/accountId` | 4 | 6 KB | ~15 MB (very active account, 10K txns) | ✅ |
| `holdings-payments` | `/accountId` | 2.8 | 4.8 KB | ~5 MB | ✅ |
| `holdings-alt-keys` | `/alternateKey` | 1 | 0.3 KB | 0.3 KB (1:1 by design) | ✅ |

### 5.3 Hot Partition TPS Check

| Container | Peak Total RU/s | Physical Partitions | Peak RU/s per Physical Partition | < 10,000 RU/s? |
|-----------|---------------:|--------------------:|--------------------------------:|:--------------:|
| `holdings-arrangements` | 26,000 | **59** ⚠️ | **441** | ✅ (storage-dominant, low RU/partition) |
| `holdings-parties` | 3,400 | 4 | 850 | ✅ |
| `holdings-balances` | 7,000 | 2 | 3,500 | ✅ |
| `holdings-transactions` | 15,000 | 3 | 5,000 | ✅ |
| `holdings-payments` | 3,000 | 3 | 1,000 | ✅ |
| `holdings-alt-keys` | 2,200 | 1 | 2,200 | ✅ |

---

## 6. Cross-Partition Pattern Analysis

### 6.1 Identified Cross-Partition / Cross-Container Fan-out Patterns

The design eliminates true cross-partition queries. However, several patterns require **cross-container fan-out** (multiple single-partition calls to different containers):

#### Pattern: AP-3 — Get Account Details (500 TPS)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Pattern: AP-3 "Get Account Details by accountId"                             │
│ Type: Cross-container (2 containers — alt-keys + arrangements)              │
│ Frequency: 500 TPS peak                                                      │
│                                                                              │
│ Flow:  ⚠️ REVISED — party hop eliminated via partySummary denormalization   │
│   Step 1: holdings-alt-keys — point read by accountId            → 1 RU     │
│   Step 2: holdings-arrangements — query by arrangementId         → 5 RU     │
│           Returns arrangement + partyArrangement (with partySummary)         │
│           + other co-located docs. Bills excluded.                           │
│                                                                              │
│   Total per request: 6 RU (was 15 RU originally)                            │
│   Total at peak: 500 × 6 = 3,000 RU/s (was 7,500 RU/s)                    │
│                                                                              │
│ Assessment: OPTIMAL — only 2 containers (alt-key resolve + arrangement      │
│   query). Party name/segment/nationality served from denormalized            │
│   partySummary in partyArrangement docs. Cross-container hop to             │
│   holdings-parties eliminated for this pattern.                              │
│                                                                              │
│ Trade-off: CP-5 propagates party detail changes to partyArrangement docs    │
│   (~50 TPS × 2 arrangements/party = 100 patch ops at 7 RU = 700 RU/s).    │
│   NET SAVINGS: 3,000 reads saved – 700 writes added = 2,300 RU/s net.      │
│                                                                              │
│ For full party details (postingRestrictions, addresses, DOB):               │
│   Additional hop to holdings-parties still needed. AP-6 still uses it.      │
└──────────────────────────────────────────────────────────────────────────────┘
```

#### Pattern: AP-4 — Get Arrangement by ID (500 TPS) ⚠️ NOW SINGLE-CONTAINER

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Pattern: AP-4 "Get Arrangement by arrangementId"                             │
│ Type: Single-container, single-partition ✅                                  │
│ Frequency: 500 TPS peak                                                      │
│                                                                              │
│ Flow:                                                                        │
│   Step 1: holdings-arrangements — query by arrangementId         → 5 RU     │
│           Returns arrangement doc + partyArrangement docs (with              │
│           partySummary: partyName, firstName, lastName,                      │
│           customerSegment, nationality).                                     │
│                                                                              │
│   Total per request: 5 RU                                                    │
│   Total at peak: 500 × 5 = 2,500 RU/s                                      │
│                                                                              │
│ Assessment: OPTIMAL — no cross-container hop. Single query returns           │
│   complete arrangement with party context. This is the ideal Cosmos DB       │
│   pattern: one partition key, one query, all data.                           │
│                                                                              │
│ ⚠️ CHANGE FROM PREVIOUS DESIGN: Previously required 2-container fan-out    │
│   (arrangements + parties). Denormalizing partySummary into                  │
│   partyArrangement eliminates the second hop.                                │
│                                                                              │
│ When full party details needed (KYC, addresses, restrictions):              │
│   Use AP-6 or direct party lookup via partyId from partyArrangement doc.    │
└──────────────────────────────────────────────────────────────────────────────┘
```

#### Pattern: AP-5 — Get Arrangements by Party (200 TPS) ⚠️ LIST-MODE OPTIMIZED

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Pattern: AP-5 "Get Arrangements by PartyId"                                  │
│ Type: Single container (list-mode ✅) / Cross-container (full-detail mode)  │
│ Frequency: 200 TPS peak                                                      │
│                                                                              │
│ Flow (LIST MODE — most common):  ⚠️ NOW SINGLE-CONTAINER                   │
│   Step 1: holdings-parties — query partyArrangement docs by partyId → 5 RU │
│           Enriched arrangementSummary contains: productGroup, productId,     │
│           productLine, status, arrangementStatus, currency, linkedReference, │
│           accountCategory, startDate, extArrangementId.                      │
│   No Step 2 needed — summary is sufficient for list responses.              │
│                                                                              │
│   Total per request: 5 RU (was 8 RU)                                       │
│   Total at peak: 200 × 5 = 1,000 RU/s (was 1,600 RU/s)                    │
│                                                                              │
│ Flow (FULL-DETAIL MODE — when API needs complete arrangement payload):      │
│   Step 1: holdings-parties — query partyArrangement docs             → 5 RU │
│   Step 2: holdings-arrangements — point read per arrangement ×3 avg  → 3 RU │
│                                                                              │
│   Total per request: 8 RU (unchanged)                                       │
│   Total at peak: 200 × 8 = 1,600 RU/s                                      │
│                                                                              │
│ Assessment: OPTIMAL for list-mode. Enriched arrangementSummary serves       │
│   arrangement list use cases from single container. Full-detail mode         │
│   retains cross-container fan-out but is less common.                        │
│                                                                              │
│ Trade-off: CP-2 propagates 10 summary fields (was 4) on arrangement         │
│   changes. Same number of patch operations, marginally larger payload.       │
│   RU per CP-2 op: 11 (was 10). Net savings: 600 - 50 = 550 RU/s.          │
└──────────────────────────────────────────────────────────────────────────────┘
```

#### Pattern: AP-7 — Get All Arrangements by Party (100 TPS)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Pattern: AP-7 "Get ALL Arrangements by PartyId (full payload)"              │
│ Type: Cross-container fan-out (2 containers)                                │
│ Frequency: 100 TPS peak                                                      │
│                                                                              │
│ Flow:                                                                        │
│   Step 1: holdings-parties — query by partyId                    → 5 RU     │
│   Step 2: holdings-arrangements — parallel point reads ×5 avg    → 15 RU    │
│                                                                              │
│   Total per request: 20 RU                                                  │
│   Total at peak: 100 × 20 = 2,000 RU/s                                     │
│                                                                              │
│ Assessment: ACCEPTABLE at current scale. Monitor for parties with           │
│   many arrangements (>20). For power users with 50+ arrangements:           │
│   Consider pagination and lazy loading.                                     │
│                                                                              │
│ ⚠ SCALE PROJECTION (if TPS grows to 500):                                  │
│   500 × 20 = 10,000 RU/s                                                   │
│   Mitigation: Richer arrangementSummary in party view to reduce             │
│   need for full arrangement fetches.                                        │
└──────────────────────────────────────────────────────────────────────────────┘
```

#### Pattern: AP-6 — Get Posting Restrictions (100 TPS)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Pattern: AP-6 "Get Posting Restrictions by accountId"                        │
│ Type: Cross-container (3 containers — alt-keys + arrangements + parties)    │
│ Frequency: 100 TPS peak                                                      │
│                                                                              │
│ Flow:                                                                        │
│   Step 1: holdings-alt-keys — point read by accountId            → 1 RU     │
│   Step 2: holdings-arrangements — query by arrangementId         → 5 RU     │
│           Returns arrangement postingRestrict + partyArrangement             │
│           (with partySummary). Bills excluded.                               │
│   Step 3: holdings-parties — point read for party-level          → 2 RU     │
│           posting restrictions (postingRestrictDetails[]).                    │
│                                                                              │
│   Total per request: 8 RU                                                   │
│   Total at peak: 100 × 8 = 800 RU/s                                        │
│                                                                              │
│ Assessment: ACCEPTABLE — 3-container hop is unavoidable because:            │
│   1. Alt-key resolve is irreducible (accountId → arrangementId)             │
│   2. Party-level posting restrictions are VOLATILE (compliance holds,        │
│      sanctions checks) — too risky to denormalize.                           │
│   3. Only 100 TPS / 800 RU/s — low total impact.                           │
│                                                                              │
│ ⚠ NOT RECOMMENDED for further optimization. Denormalizing posting           │
│   restrictions would require propagating every restriction change to all     │
│   linked arrangements (~50 arrangements for corporate parties). Write        │
│   amplification risk with compliance-sensitive data outweighs the            │
│   ~200 RU/s savings.                                                        │
└──────────────────────────────────────────────────────────────────────────────┘
```

#### Pattern: AP-9 — Bulk Arrangements (100 TPS) ⚠️ RU CORRECTED

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Pattern: AP-9 "Bulk Get Arrangements by arrangementId[]"                     │
│ Type: Single-container (arrangements only) ✅                                │
│ Frequency: 100 TPS peak                                                      │
│                                                                              │
│ Flow:  ⚠️ REVISED — alt-key hop removed (input is arrangementId)           │
│   For each arrangementId in batch (avg 5):                                  │
│     arrangementsContainer.queryItems(...)            → 5 RU each            │
│   Parallelized.                                                              │
│                                                                              │
│   Total per request: 5 × 5 = 25 RU (was 50 RU)                            │
│   Total at peak: 100 × 25 = 2,500 RU/s (was 5,000 RU/s)                   │
│                                                                              │
│ ⚠️ CORRECTIONS APPLIED:                                                    │
│   1. Alt-key step removed — API input is arrangementId, not accountId.      │
│      Original RDBMS sequence resolved altKeys because of ORM join paths;    │
│      Cosmos DB goes direct-to-partition by arrangementId.                    │
│   2. RU per arrangement reduced from 10 to 5 — core arrangement doc is     │
│      now ~3 KB (bills extracted to separate docs per Section 12).           │
│      Query returns arrangement + partyArrangement + co-located docs.        │
│                                                                              │
│ Assessment: OPTIMAL after corrections — parallelized single-partition       │
│   queries, no cross-container fan-out.                                       │
│                                                                              │
│ If batch includes non-arrangementId identifiers (e.g., accountId):          │
│   Pre-resolve via holdings-alt-keys, then direct to arrangements.            │
│   Add 1 RU per alt-key resolve.                                             │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 6.2 Systematic Cross-Container Optimization Evaluation

All access patterns with cross-container hops were evaluated for further optimization. The guiding principle: **optimize where savings are material and do not introduce scalability risks or excessive write amplification.**

| Pattern | Current Hops | Containers | TPS | Current RU/op | Peak RU/s | Verdict | Action | Savings |
|:--------|:------------|:-----------|----:|-------------:|---------:|:--------|:-------|--------:|
| AP-3 | 2 | alt-keys → arrangements | 500 | 6 | 3,000 | ❌ **IRREDUCIBLE** | None — alt-key resolve is the canonical Cosmos pattern for non-PK access (1 RU point read). Cannot put `accountId` as PK on arrangements without breaking all other patterns. | 0 |
| AP-5 | 2 | parties → arrangements | 200 | 8 | 1,600 | ✅ **ENRICHABLE** | Enrich `arrangementSummary` in party view so list-mode (most common AP-5 use) serves from single container. Full-detail mode still fans out. | **600 RU/s** |
| AP-6 | 3 | alt-keys → arrangements → parties | 100 | 8 | 800 | ❌ **TOO VOLATILE** | Posting restrictions are compliance-sensitive, party-scoped, and frequently updated. Denormalizing them into arrangements creates write amplification risk (50+ patches per restriction change for corporate parties). Only 800 RU/s total. | 0 |
| AP-7 | 2 | parties → arrangements | 100 | 20 | 2,000 | ❌ **WRITE AMP > READ SAVINGS** | Full payload duplication requires syncing entire arrangement docs to party view. At 200 TPS arrangement writes × avg 2 parties = 400 patches × 10 RU = 4,000 RU/s write cost vs. 2,000 RU/s read cost. Net negative at current scale. Monitor for 3× trigger. | 0 |
| AP-9 | 2→1 | ~~alt-keys~~ → arrangements | 100 | 50→**25** | 5,000→**2,500** | ✅ **RU CORRECTED** | (a) Alt-key hop removed — API input is `arrangementId`, Cosmos goes direct-to-partition. (b) Per-arrangement RU corrected from 10 to 5 (doc size ~3 KB after bill extraction). | **2,500 RU/s** |

**Summary of Round 3 Optimization:**
- **Patterns optimized:** AP-5 (enriched list-mode), AP-9 (RU correction + alt-key removal)
- **Patterns left as-is:** AP-3 (irreducible), AP-6 (too volatile), AP-7 (write amp exceeds savings)
- **Gross read savings:** 3,100 RU/s
- **Additional CP-2 cost** (enriched summary sync): +50 RU/s
- **Net savings:** ~3,050 RU/s

**Cumulative optimization impact (all rounds):**

| Round | Optimization | Patterns Improved | Net RU/s Saved |
|:------|:-----------|:-----------------|---------------:|
| 1 | Bill extraction (Section 12) | AP-3, AP-4, AP-8, AP-9, AP-10 | ~5,000 (doc size reduction) |
| 2 | partySummary denormalization | AP-3, AP-4, AP-6 | ~1,800 |
| 3 | arrangementSummary enrichment + AP-9 correction | AP-5, AP-9 | ~3,050 |
| **Total** | | | **~9,850 RU/s** |

### 6.3 Scale Impact — RU Progression with Volume and TPS

The table below projects RU growth for the key cross-container patterns as data volume and TPS scale:

#### AP-5 / AP-7 — Party-to-Arrangement Fan-out

> **⚠️ R3 Note:** AP-5 list-mode now serves from `holdings-parties` only (5 RU, single container). The table below shows the **full-detail mode** (cross-container fan-out) which is now only needed when the API requires complete arrangement payloads.

| Scenario | Arrangements Volume | Parties Volume | Physical Partitions (arrangements) | Avg Arrangements/Party | AP-5 RU/op (list / full) | AP-5 TPS | AP-5 Peak RU/s (list / full) | AP-7 RU/op | AP-7 TPS | AP-7 Peak RU/s |
|:---------|-------------------:|---------------:|-----------------------------------:|-----------------------:|----------:|--------:|---------------:|----------:|--------:|---------------:|
| **Current** | 32.5M | 15M | 8 | 3 | **5** / 8 | 200 | **1,000** / 1,600 | 20 | 100 | 2,000 |
| **Year 1 (+20%)** | 39M | 18M | 10 | 3 | **5** / 8 | 240 | **1,200** / 1,920 | 20 | 120 | 2,400 |
| **Year 2 (+50%)** | 49M | 22.5M | 12 | 3.5 | **5** / 9 | 300 | **1,500** / 2,700 | 23 | 150 | 3,450 |
| **Year 3 (2×)** | 65M | 30M | 16 | 4 | **5** / 10 | 400 | **2,000** / 4,000 | 28 | 200 | 5,600 |
| **Stress (5×)** | 162.5M | 75M | 38 | 5 | **5** / 13 | 1,000 | **5,000** / 13,000 | 35 | 500 | 17,500 |

> **Key observation:** Fan-out cost scales linearly with `arrangements_per_party`, NOT with total data volume. The physical partition count only matters for cross-partition queries (which we avoid). Single-partition point reads remain ~1 RU regardless of container size.

#### Alternative Implementation for Stress Scenario (AP-5/AP-7 fan-out)

If fan-out RU becomes prohibitive at 5× scale:

| Approach | Description | Trade-off | Projected RU Savings |
|:---------|:-----------|:----------|--------------------:|
| **Richer Materialized View** | Embed full arrangement payload (not just summary) in `holdings-parties` partyArrangement docs via Change Feed | Increased write amplification: every arrangement update triggers party view update. Storage duplication ~2×. | 80% read savings — AP-5/AP-7 become single-container queries. At 5× stress: 13,000 → 2,600 RU/s |
| **Hierarchical Partition Key on Arrangements** | Use HPK `/partyId`/`/arrangementId` on a dedicated party-arrangement container | Requires moving party-arrangement data to a new container. Enables efficient `partyId`-scoped queries. | 50% savings on fan-out. Adds container complexity. |
| **API-Level Caching (Redis/CDK)** | Cache arrangement summaries for 30-60 seconds | Slightly stale data. Effective for repeat reads (e.g., UI pagination). | 70-90% cache hit rate at high TPS → proportional RU reduction |

**Recommendation at current scale:** Current cross-container fan-out design with materialized `arrangementSummary` in party view. Monitor RU/s on both containers. At 3× scale, evaluate richer materialized view.

---

## 7. RU & Storage Estimates

### 7.1 Per-Container Summary

| Container | Entity Types | Records | Avg Doc Size (KB) | Storage (GB) | Read RU/s | Write RU/s | Compensating RU/s | Total RU/s | Autoscale Max RU/s | Physical Partitions |
|-----------|:------------|--------:|---------:|--------:|----------:|---------:|------------------:|----------:|-------------------:|--------------------:|
| `holdings-arrangements` | arrangement, partyArrangement, dueDiligence, paymentSchedule, contractActivity, contractBalance, postingRestrict, savingsPot, **arrangementBill** ⚠️ NEW | **~6,675.5M** ⚠️ | **0.4** ⚠️ | **2,930** ⚠️ | **8,800** ⚠️ R3 | 8,500 | 500 (CP-3 alt-key sync source) | **17,800** ⚠️ R3 | 26,000 | **59** ⚠️ |
| `holdings-parties` | partyDetails, partyArrangement (materialized, **enriched summary** ⚠️ R3), customerValues | 95M | **2.2** | **199** | 1,900 | 750 | **1,250** (CP-1 + CP-2 enriched) | **3,900** | 5,000 | 4 |
| `holdings-balances` | balance | 100M | 1.0 | 95 | 3,000 | 4,000 | — | 7,000 | 8,000 | 2 |
| `holdings-transactions` | transaction | 100M | 1.5 | 143 | 10,000 | 5,000 | — | 15,000 | 18,000 | 3 |
| `holdings-payments` | paymentOrder, paymentTransaction | 70M | 1.7 | 113 | 1,000 | 2,000 | — | 3,000 | 4,000 | 3 |
| `holdings-portfolio` | portfolioValues, portfolioHoldings, portfolioAllocation, customerAllocation, instrument | 14M | 1.1 | 15 | 500 | 200 | — | 700 | 1,000 | 1 |
| `holdings-events` | eventProcessed, eventAggregator | 25M | 0.9 | 21 | 200 | 2,000 | — | 2,200 | 3,000 | 1 |
| `holdings-reference` | all reference types (11) | 53K | 0.5 | 0.03 | 100 | 5 | — | 105 | Serverless | 1 |
| `holdings-alt-keys` | altKeyLookup | 130M | 0.3 | 37 | **1,100** ⚠️ R3 | 500 | — | **1,600** ⚠️ R3 | 3,000 | 1 |
| **TOTAL** | | **~7,209.5M** ⚠️ | | **~3,553 GB** | **26,600** ⚠️ R3 | **22,955** | **1,750** ⚠️ R3 | **51,305** ⚠️ R3 | **68,000** | **74** ⚠️ |

> **⚠️ R3 = Round 3 optimizations applied.** AP-5 list-mode now single-container (−600 read RU/s from arrangements). AP-9 corrected to 5 RU/arrangement after bill extraction (−2,500 RU/s from arrangements, −500 RU/s from alt-keys). CP-2 marginally increased (+50 RU/s for enriched summary fields). **Net savings: ~3,050 RU/s.**

### 7.2 Detailed RU Breakdown per Access Pattern

| # | Pattern | Container(s) | Operation | RU/op | Avg TPS | Peak TPS | Avg RU/s | Peak RU/s |
|--:|---------|:------------|:----------|------:|--------:|---------:|---------:|----------:|
| AP-1 | Get Balance | balances | Single-partition query | 3 | 500 | 1,000 | 1,500 | 3,000 |
| AP-2 | Get Transactions | transactions | Single-partition query (paginated) | 10 | 500 | 1,000 | 5,000 | 10,000 |
| AP-3 | Get Account Details | alt-keys → arrangements | Point read + single-partition query | 6 | 250 | 500 | 1,500 | 3,000 |
| AP-4 | Get Arrangement | **arrangements only** ✅ | **Single-partition query** | **5** | 250 | 500 | **1,250** | **2,500** |
| AP-5 | Get Party Arrangements | **parties only** ✅ (list) / parties → arrangements (full) | **Single-partition query (list)** / Query + fan-out (full) | **5** (list) / 8 (full) | 100 | 200 | **500** (list) / 800 (full) | **1,000** (list) / 1,600 (full) |
| AP-6 | Get Posting Restrictions | alt-keys → arrangements → parties | Point read + query + point reads | 8 | 50 | 100 | 400 | 800 |
| AP-7 | Get All Party Arrangements | parties → arrangements | Query + point reads ×5 | 20 | 50 | 100 | 1,000 | 2,000 |
| AP-8 | Get Schedules | arrangements | Single-partition query | 8 | 50 | 100 | 400 | 800 |
| AP-9 | Bulk Arrangements | **arrangements only** ✅ | **Parallel single-partition queries** | **25** | 50 | 100 | **1,250** | **2,500** |
| **AP-10** | **⚠️ Get Bills** | **arrangements** | **Single-partition query (paginated)** | **8** | **100** | **200** | **800** | **1,600** |
| WP-1 | Create/Update Arrangement | arrangements | Upsert | 15 | 100 | 200 | 1,500 | 3,000 |
| WP-2 | Update Balance | balances | Replace | 8 | 250 | 500 | 2,000 | 4,000 |
| WP-3 | Create Transaction | transactions | Create | 10 | 250 | 500 | 2,500 | 5,000 |
| WP-4 | Update Party-Arrangement | arrangements | Upsert | 7 | 50 | 100 | 350 | 700 |
| WP-5 | Create Payment | payments | Create | 10 | 100 | 200 | 1,000 | 2,000 |
| WP-6 | Update Party | parties | Patch | 10 | 25 | 50 | 250 | 500 |
| WP-7 | Process Event | events | Create | 8 | 100 | 200 | 800 | 1,600 |
| **WP-8** | **⚠️ Create Bill** | **arrangements** | **Create** | **7** | **280** | **560** | **1,960** | **3,920** |
| CP-1 | Sync PartyArr → Parties | parties | Change Feed → Upsert | 7 | 50 | 100 | 350 | 700 |
| CP-2 | Sync Arr Summary → Parties | parties | Change Feed → Patch (**enriched** ⚠️ R3) | **11** | 25 | 50 | **275** | **550** |
| CP-3 | Sync AltKey Lookup | alt-keys | Change Feed → Upsert | 5 | 50 | 100 | 250 | 500 |
| CP-4 | Balance Write-through | balances | Replace (app-level) | — | — | — | Included in WP-2 | — |

### 7.3 Physical Partition & Storage Validation

```
For each container:

  ── holdings-arrangements ──
  Storage:            2,930 GB ⚠️ (was 368 GB — bill docs add ~2,600 GB)
  Total RU/s:         17,800 ⚠️ R3 (autoscale max: 26,000)
  Phys. partitions:   MAX(CEIL(2930/50), CEIL(26,000/10,000)) = MAX(59, 3) = 59
  RU per phys. part:  26,000 / 59 = 441 ✅ (<10,000) — storage-dominant
  Min RU floor:       2,930 × 10 = 29,300 (autoscale) → ⚠️ 29,300 > 26,000
  ⚠️ ATTENTION: Storage-driven min RU floor (29,300) exceeds
     workload-driven autoscale max (26,000). Must provision at least
     29,300 RU/s autoscale max to avoid throttling. Alternatively,
     consider archiving old bills or using TTL on bill documents.

  ── holdings-parties ──
  Storage:            190 GB
  Total RU/s:         3,850 (autoscale max: 5,000)
  Phys. partitions:   MAX(CEIL(190/50), CEIL(5,000/10,000)) = MAX(4, 1) = 4
  RU per phys. part:  5,000 / 4 = 1,250 ✅
  Min RU floor:       190 × 10 = 1,900 → 5,000 ≥ 1,900 ✅

  ── holdings-balances ──
  Storage:            95 GB
  Total RU/s:         7,000 (autoscale max: 8,000)
  Phys. partitions:   MAX(CEIL(95/50), CEIL(8,000/10,000)) = MAX(2, 1) = 2
  RU per phys. part:  8,000 / 2 = 4,000 ✅
  Min RU floor:       95 × 10 = 950 → 8,000 ≥ 950 ✅

  ── holdings-transactions ──
  Storage:            143 GB
  Total RU/s:         15,000 (autoscale max: 18,000)
  Phys. partitions:   MAX(CEIL(143/50), CEIL(18,000/10,000)) = MAX(3, 2) = 3
  RU per phys. part:  18,000 / 3 = 6,000 ✅
  Min RU floor:       143 × 10 = 1,430 → 18,000 ≥ 1,430 ✅

  ── holdings-payments ──
  Storage:            113 GB
  Total RU/s:         3,000 (autoscale max: 4,000)
  Phys. partitions:   MAX(CEIL(113/50), CEIL(4,000/10,000)) = MAX(3, 1) = 3
  RU per phys. part:  4,000 / 3 = 1,333 ✅
  Min RU floor:       113 × 10 = 1,130 → 4,000 ≥ 1,130 ✅

  ── holdings-portfolio ──
  Storage:            15 GB
  Total RU/s:         700 (autoscale max: 1,000)
  Phys. partitions:   MAX(CEIL(15/50), CEIL(1,000/10,000)) = MAX(1, 1) = 1
  Min RU floor:       15 × 10 = 150 → 1,000 ≥ 150 ✅

  ── holdings-events ──
  Storage:            21 GB (with TTL: effective ~7 GB at steady state)
  Total RU/s:         2,200 (autoscale max: 3,000)
  Phys. partitions:   MAX(CEIL(21/50), CEIL(3,000/10,000)) = MAX(1, 1) = 1
  Min RU floor:       21 × 10 = 210 → 3,000 ≥ 210 ✅

  ── holdings-alt-keys ──
  Storage:            37 GB
  Total RU/s:         2,100 (autoscale max: 3,000)
  Phys. partitions:   MAX(CEIL(37/50), CEIL(3,000/10,000)) = MAX(1, 1) = 1
  Min RU floor:       37 × 10 = 370 → 3,000 ≥ 370 ✅
```

---

## 8. Relationship Mapping

| Source (RDBMS) | Relationship | Cosmos DB Pattern | Container(s) | Rationale |
|:---------------|:------------|:-----------------|:------------|:----------|
| Arrangement → (14 @Embeddable children) | 1:1 / 1:Few (bounded) **EXCEPT ArrangementBills** | **13 embedded** in arrangement document; **ArrangementBills → separate `arrangementBill` docs** | `holdings-arrangements` | JPA models these as @Embeddable. Access correlation >95%. Combined size ~3KB (core doc, measured from real sample). `model-embed-related`. **⚠️ ArrangementBills EXTRACTED** — 1:N UNBOUNDED (~1.53/day, observed 472 in 10 months). Separate `arrangementBill` docs in same container. `model-reference-large`, `model-avoid-2mb-limit`. See Section 12.** |
| Arrangement ↔ PartyArrangement ↔ PartyDetails | M:N via junction table | **Multi-doc same container** (by arrangementId) + **Materialized view** (by partyId) | `holdings-arrangements` (primary) + `holdings-parties` (view) | PartyArrangement must be queryable by both arrangementId (AP-3/4) and partyId (AP-5/7). Dual storage via Change Feed. `model-reference-large`, `pattern-change-feed-materialized-views` |
| Arrangement → BusinessContractActivity | 1:N (bounded, ~1-5 per arrangement) | **Multi-doc same container** (type: `contractActivity`) | `holdings-arrangements` | Queried with arrangement in AP-3. Bounded count. Same partition key. `model-type-discriminator` |
| Arrangement → BusinessContractBalance | 1:1 per contract | **Multi-doc same container** (type: `contractBalance`) | `holdings-arrangements` | Always fetched with arrangement. `model-embed-related` |
| Arrangement → PaymentSchedules | 1:N (bounded, ~1-20 per arrangement) | **Multi-doc same container** (type: `paymentSchedule`) | `holdings-arrangements` | Bounded. Queried together in AP-8. `model-type-discriminator` |
| Arrangement → DueDiligence | 1:1 | **Multi-doc same container** (type: `dueDiligence`) | `holdings-arrangements` | Low frequency access but shares PK. `model-type-discriminator` |
| PartyDetails → PostingRestrictDetails | 1:Few | **Embedded** array in partyDetails document | `holdings-parties` | Bounded, small, always fetched with party (AP-6). `model-embed-related` |
| Account → Balance | 1:Few | **Separate container** | `holdings-balances` | 100M records, 1000 TPS hot path, different update frequency from arrangement. `model-reference-large` |
| Account → Transaction | 1:N (unbounded) | **Separate container** | `holdings-transactions` | 100M records, 1000 TPS, unbounded growth. `model-reference-large` |
| Account → PaymentOrder/PaymentTransaction | 1:N (unbounded) | **Separate container** | `holdings-payments` | Separate write path, different lifecycle. `model-reference-large` |
| Arrangement → PortfolioHoldings/Values | 1:N | **Separate container** | `holdings-portfolio` | Investment-specific, different access patterns, optional. `model-reference-large` |
| MsAltKey → Arrangement/Party | N:1 reverse lookup | **Separate lookup container** + embedded summary in parent | `holdings-alt-keys` (lookup), embedded `alternateKeys[]` in arrangement/party docs | Resolves non-PK access (accountId→arrangementId). Point reads at ~1 RU. `model-denormalize-reads` |
| All reference tables (CountryCodes, PartyRoles, Status, etc.) | Lookup / FK | **Consolidated reference container** with synthetic PK | `holdings-reference` | Low volume, rarely changing, cacheable. `model-container-consolidation`, `model-type-discriminator` |

---

## 9. Denormalization Register

| Duplicated Data | Source Entity | Source Container | Target Entity | Target Container | Propagation Strategy | Frequency | RU Cost |
|:---------------|:------------|:----------------|:-------------|:----------------|:--------------------|:----------|--------:|
| `partyArrangement` docs | PartyArrangement | `holdings-arrangements` | partyArrangement (materialized) | `holdings-parties` | Change Feed processor: filter `type = 'partyArrangement'`, upsert to parties container with partyId as PK | On PartyArrangement create/update | 7 RU/op |
| `arrangementSummary` (productGroup, productId, productLine, status, arrangementStatus, currency, **linkedReference, accountCategory, startDate, extArrangementId** ⚠️ ENRICHED R3) | Arrangement | `holdings-arrangements` | partyArrangement.arrangementSummary | `holdings-parties` | Change Feed processor: filter `type = 'arrangement'`, patch summary fields in all linked partyArrangement docs. ⚠️ R3: 6 additional fields propagated. | On Arrangement status/product/detail change | 11 RU/op × N parties |
| `alternateKeys[]` array | MsAltKey | `holdings-alt-keys` | arrangement.alternateKeys[], partyDetails.alternateKeys[] | `holdings-arrangements`, `holdings-parties` | Application-level: on altKey upsert, also embed in parent document. OR Change Feed from alt-keys → parent containers. | On AltKey create/update | 5 RU/op |
| `partySummary` (partyName, firstName, lastName, customerSegment, nationality) | PartyDetails | `holdings-parties` | partyArrangement.partySummary | `holdings-arrangements` | **CP-5:** Change Feed processor on `holdings-parties`: on partyDetails change, patch `partySummary` in all linked partyArrangement docs in arrangements container. Avg 2 arrangements per party. | Rare (~50 TPS → ~100 patch ops) | 7 RU/op × ~2 arrangements = 14 RU per party update |
| `productDetails` (summary) | ProductDetails | `holdings-reference` | arrangement.productDetails | `holdings-arrangements` | Manual/batch: product changes are rare. Full resync if needed. | Near-zero | Batch job |

---

## 10. Validation Checklists

### 10a. Partition Key Validation

| Check | Rule | Status |
|:------|:-----|:------:|
| All P0 queries are single-partition | `query-avoid-cross-partition` | ✅ All P0 patterns (AP-1 through AP-5) resolve to single-partition operations per container |
| No logical partition exceeds 20 GB | `partition-20gb-limit` | ✅ Max logical partition ~15 MB (transactions for very active account) |
| No hot partition exceeds 10K RU/s at peak | `partition-avoid-hotspots` | ✅ Max per physical partition: 6,000 RU/s (transactions) |
| Write distribution is even across partitions | `partition-high-cardinality` | ✅ All PK fields have >15M cardinality |
| HPK considered where synthetic keys are used | `partition-synthetic-keys` | ℹ️ Only `holdings-reference` uses synthetic PK; volume is trivial — HPK not needed |

### 10b. Data Model Validation

| Check | Rule | Status |
|:------|:-----|:------:|
| No document exceeds 1 MB | `model-avoid-2mb-limit` | ✅ **CORRECTED** | Core arrangement doc ~3 KB (measured from real sample). Bill docs ~0.4 KB each. **⚠️ Original estimate of ~15 KB was WRONG. Real sample showed 187 KB with embedded bills. Bills now extracted.** |
| No unbounded arrays embedded | `model-reference-large` | ✅ **CORRECTED** | **⚠️ Original assessment was WRONG — `arrangementBills` was unbounded (472 items, 183.5 KB, growing at 1.53/day). Now extracted to separate `arrangementBill` documents. All remaining embedded arrays are bounded.** |
| Multi-entity containers have `type` discriminator | `model-type-discriminator` | ✅ All containers except `holdings-balances` use `type` field |
| Denormalized fields have propagation strategy | `model-denormalize-reads` | ✅ See Denormalization Register (Section 9) |
| Identifying relationships use parent_id as PK | `model-identifying-relationships` | ✅ PartyArrangement, PaymentSchedule, ContractActivity all co-located by parent arrangementId |
| Container consolidation evaluated | `model-container-consolidation` | ✅ 34 tables → 9 containers (3.8:1 consolidation) |

### 10c. Query & Index Validation

| Check | Rule | Status |
|:------|:-----|:------:|
| ORDER BY queries have composite indexes | `index-composite` | ✅ Transactions (processingDate DESC), Payments (executionDate DESC), Events (eventDate DESC) |
| Unused paths excluded from indexing | `index-exclude-unused` | ✅ All containers exclude nested objects not used in WHERE/ORDER BY (emailAddresses, correspondenceAddresses, lockedAmounts, etc.) |
| Queries use projections | `query-use-projections` | ✅ API responses should use `SELECT c.id, c.status, c.amount ...` not `SELECT *` |
| Queries are parameterized | `query-parameterize` | ✅ All queries use `@paramName` syntax |
| Large result sets use pagination | `query-pagination` | ✅ Transaction queries (AP-2) use continuation tokens |

### 10d. Operational Validation

| Check | Rule | Status |
|:------|:-----|:------:|
| Throughput mode appropriate | `throughput-autoscale` | ✅ Autoscale for all containers (variable banking workload). Serverless for reference data. |
| TTL used for transient data | `pattern-ttl-transient-data` | ✅ `holdings-events` has 90-day TTL on event documents |
| Monitoring plan | `monitoring-ru-consumption`, `monitoring-latency` | ⚠️ Plan needed: Azure Monitor alerts for >80% RU utilization, P99 latency >50ms, 429 throttling rate |
| SDK singleton documented | `sdk-singleton-client` | ⚠️ Implementation guidance: use `CosmosClient` as singleton via DI. Direct connection mode for production. |
| Change feed identified | `pattern-change-feed-materialized-views` | ✅ 3 Change Feed processors: arrangements→parties (CP-1/CP-2), arrangements→alt-keys (CP-3) |

### 10e. Scale Readiness

| Check | Rule | Status |
|:------|:-----|:------:|
| Write-heavy entities use appropriate strategies | `pattern-data-binning` | ✅ Transaction creates (500 TPS) distributed across 25M accounts — no binning needed |
| Known hot keys use write sharding | `pattern-write-sharding` | ✅ No identified hot keys — all PKs have high cardinality |
| Multi-region requirements | `global-multi-region` | ⚠️ To be defined: active-active vs active-passive, consistency level (Session recommended for banking) |
| Burst capacity | `throughput-burst` | ✅ Autoscale provides 10× burst. Peak utilization ~80% of autoscale max. |

---

## 11. Migration Pitfalls Addressed

| # | Anti-Pattern | Status | How Addressed |
|--:|:------------|:------:|:-------------|
| 1 | 1:1 table-to-container mapping | ✅ Avoided | 34 tables consolidated into 9 containers based on access pattern analysis |
| 2 | Join tables preserved as containers | ✅ Avoided | PartyArrangement (junction table) stored as multi-doc in arrangements container + materialized view in parties container |
| 3 | Foreign key columns without embedding or identifying relationships | ✅ Avoided | All FK relationships converted to embedding (bounded children), co-location (same PK), or materialized views |
| 4 | Auto-increment IDs | ✅ Avoided | All IDs use natural keys (arrangementId, partyId, accountId) or composite natural keys (container-specific prefixed IDs) |
| 5 | Cross-partition queries for primary access paths | ✅ Avoided | All P0 patterns are single-partition. Cross-container fan-out parallelized. |
| 6 | Unbounded arrays embedded | ✅ **CORRECTED** | **⚠️ Originally assessed as avoided, but real sample proved `arrangementBills` is UNBOUNDED (472 items in 10 months, 183.5 KB, growing at 1.53/day). 5-year projection: 1.1 MB. NOW FIXED: bills extracted to separate `arrangementBill` documents in same container.** Transactions, events, payments as separate containers. |
| 7 | No `type` discriminator on multi-entity containers | ✅ Avoided | All multi-entity containers use `type` field for document discrimination |
| 8 | Partition key chosen without access pattern analysis | ✅ Avoided | PK selection driven by 9 analyzed access patterns + TPS validation |
| 9 | No cost estimate comparing alternatives | ✅ Addressed | RU estimates provided per pattern; scale projections for fan-out patterns; alternative implementations evaluated for stress scenarios |

---

## 12. Sample Document Analysis & Design Revision

> **Source:** `SampleDocument_holdings.json` — Real production Arrangement entity document from customer environment.

### 12.1 Sample Document Measurements

| Metric | Value |
|:-------|------:|
| File size (formatted JSON) | 337,712 bytes (329.8 KB) |
| **Minified size** | **~186.6 KB** |
| Total top-level properties | 27 |
| ArrangementId | `AA251130003V` |
| Start date | 2025-04-23 |
| Processing date | 2026-02-26 |
| Document age at sample time | ~308 days |

### 12.2 Property Size Breakdown

| Property | Type | Count/Size | % of Doc |
|:---------|:-----|:-----------|:---------|
| **`arrangementBills`** | **array** | **472 items, 183.5 KB** | **98.4%** |
| `scheduleDetails` | array | 6 items, 1.0 KB | 0.5% |
| `arrangementInterest` | array | 1 item, 0.3 KB | 0.2% |
| `accountServices` | array | 2 items, 0.3 KB | 0.2% |
| `productDetails` | object | 0.3 KB | 0.2% |
| `companyDetails` | object | 0.2 KB | 0.1% |
| `accountArrangement` | object | 0.1 KB | 0.1% |
| Other scalar properties | — | ~0.6 KB | 0.3% |
| **Base document (without bills)** | — | **~3.0 KB** | — |

### 12.3 arrangementBills Analysis

| Metric | Value |
|:-------|------:|
| Total bills in sample | 472 |
| Avg bill size (minified) | ~396 bytes |
| Bill types: ACT.CHARGE | 394 (83.5%) |
| Bill types: PAYMENT | 79 (16.5%) |
| Growth rate (472 bills / 308 days) | **1.53 bills/day** |
| Growth rate per year | **~560 bills/year** |

### 12.4 Growth Projections (if bills remained embedded)

| Age | Est. Bills | Est. Doc Size | Status |
|:----|----------:|:-----------|:-------|
| 1 year | 560 | **219 KB** | ⚠️ Heavy but under limit |
| 2 years | 1,120 | **436 KB** | ⚠️ Approaching danger zone |
| 3 years | 1,680 | **652 KB** | 🔴 Over 50% of 1 MB warning threshold |
| **5 years** | **2,800** | **1.1 MB** | **🔴 EXCEEDS 1 MB — violates `model-avoid-2mb-limit`** |
| 7 years | 3,920 | **1.5 MB** | 🔴 EXCEEDS 1.5 MB — near hard 2 MB limit |

### 12.5 Critical Finding & Design Impact

> **⚠️ CRITICAL:** The original design embedded `arrangementBills` as an array in the arrangement document. Real production data proves this is an **unbounded, rapidly-growing array** that will breach the Cosmos DB 2 MB document size limit for long-lived arrangements.

**Original estimate vs. reality:**

| Metric | Original Estimate | Actual (from sample) | Error Factor |
|:-------|:------------------|:--------------------|:-------------|
| Arrangement doc size | 2.0 KB | 187 KB (with bills) | **93×** |
| Max doc size | ~15 KB | 1.5 MB at 7 years | **100×** |
| Bills count | "bounded <50" | 472 (and growing) | **9.4×** at 10 months |
| Arrangement container storage | 390 GB | ~2,930 GB | **7.5×** |

### 12.6 Design Correction Applied

**ArrangementBills extracted to separate `arrangementBill` documents:**

- Each bill is now a separate document (~0.4 KB) in the `holdings-arrangements` container
- Partition key: `arrangementId` (same as parent — single-partition queries preserved)
- Type discriminator: `type = "arrangementBill"`
- Core arrangement document reduced to **~3 KB** (measured)
- Bills queried separately with pagination: `WHERE type = 'arrangementBill' ORDER BY billDate DESC`
- Composite index added: `(type ASC, billDate DESC)` for efficient chronological queries

**Impact on container:**

| Metric | Before (bills embedded) | After (bills extracted) |
|:-------|:-----------------------|:----------------------|
| Core arrangement doc size | 187 KB | **~3 KB** |
| Docs per logical partition | 5.4 | **~205** (core docs + ~200 avg bill docs) |
| Data per logical partition | 12 KB | **~85 KB** |
| Total docs in container | 175.5M | **~6.68B** (175.5M core + ~6.5B bills) |
| Total storage | 390 GB | **~2,930 GB** |
| Physical partitions needed | 8 | **59** (storage-dominant) |
| Arrangement read RU (AP-3/AP-4) | 12-15 RU | **5 RU** (smaller doc) |
| New bill write RU (WP-8) | N/A | **7 RU × ~560 TPS = 3,920 RU/s** |

### 12.7 Field Name Corrections (from real sample)

The real production document uses different field names than the original JPA entity model suggested:

| Original Assumption | Real Field Name | Notes |
|:-------------------|:---------------|:------|
| `linkedAccountId` | `linkedReference` | Account reference number |
| `status` | `arrangementStatus` | e.g., "CURRENT" |
| `productId` | `productLine` / `productGroup` | Separate fields |
| `openingDate` | `startDate` | ISO date string |
| N/A (not modeled) | `businessKey` | Format: `coretransact\|US0010001\|{arrId}` |
| N/A (not modeled) | `branch`, `country`, `legalEntityId` | Geography/entity context |
| N/A (not modeled) | `extArrangementId`, `systemReference` | External system references |
| N/A (not modeled) | `isPortFolio`, `isPortFolioAccount` | Boolean flags |
| N/A (not modeled) | `externalIndicator`, `estmtEnabled` | Feature flags |
| `accountArrangement` (rich object) | `accountArrangement` (minimal) | Only `extensionData` + `processingDate` |
| `arrangementInterest` (single object) | `arrangementInterest` (array) | Contains `fixedRate`, `effectiveRate`, `dividentPaidYtd`, `interestAccrued`, etc. |

### 12.8 Recommendations & Open Items from Sample Analysis

| # | Recommendation | Priority | Status |
|--:|:-------------|:---------|:-------|
| 1 | **Extract `arrangementBills` to separate documents** — MANDATORY for Cosmos DB compliance | P0 | ✅ Applied in this plan revision |
| 2 | **Validate bill growth rate across arrangement types** — Current sample is a Current Account. Lending arrangements may have different bill rates. | P0 | ⚠️ Need samples from Lending, Deposit, Portfolio types |
| 3 | **Consider TTL on bill documents** — If bills >3 years are rarely accessed, apply TTL + archive to cold storage (Blob/ADL) | P1 | 📋 To evaluate with Business |
| 4 | **Validate all field names against actual API contracts** — Several field names differ from JPA entity model | P0 | ⚠️ Need API schema review |
| 5 | **Collect more sample documents** — One sample is insufficient. Need distribution: min/avg/P95/max sizes across arrangement types. | P0 | ⚠️ Pending |
| 6 | **Evaluate separate `holdings-bills` container** — At ~6.5B bill docs and 2.9 TB, consider whether bills warrant their own container to avoid storage-dominant partition provisioning on the arrangements container. Trade-off: adds a cross-container read for bill queries. | P1 | 📋 Design review item |
| 7 | **Review `extensionData` field** — Empty in sample (`{}`) but may contain variable-length data in other arrangement types. Could impact sizing. | P1 | ⚠️ Need more samples |

---

## 13. Spring Boot Integration Recommendations

> **Context:** The Temenos Holdings microservice currently uses **Spring Boot with JPA/Hibernate** against an RDBMS. This section provides prescriptive guidance for migrating to **Azure Cosmos DB for NoSQL** using the Java SDK with Spring Boot.

### 13.1 Technology Stack

| Layer | Current (RDBMS) | Target (Cosmos DB) | Notes |
|:------|:---------------|:------------------|:------|
| Framework | Spring Boot 3.x | Spring Boot 3.x | No change — same framework |
| Java Version | Java 17+ | Java 17+ | Required by Spring Boot 3.x. **`sdk-java-spring-boot-versions`** |
| Data Access | Spring Data JPA + Hibernate | **Azure Spring Data Cosmos** (`azure-spring-data-cosmos`) | Repository pattern preserved. Spring Data Cosmos replaces JPA. |
| Low-level SDK | JDBC driver | **Azure Cosmos DB Java SDK v4** (`com.azure:azure-cosmos`) | For Change Feed processors, bulk operations, transactional batch — where Spring Data abstraction is insufficient. |
| Entity Annotations | `@Entity`, `@Table`, `@Column`, `@Id` (JPA) | `@Container`, `@PartitionKey`, `@Id` (Spring Data Cosmos) | **`sdk-spring-data-annotations`** — all JPA annotations must be replaced. |
| Repositories | `extends JpaRepository<T, Long>` | `extends CosmosRepository<T, String>` | IDs must be `String`. `Iterable` return types. **`sdk-spring-data-repository`** |
| Connection | DataSource + HikariCP | `CosmosClient` singleton `@Bean` | **`sdk-singleton-client`** — single client instance, never per-request. |
| Transactions | `@Transactional` (JPA/JDBC) | `CosmosBatch` (same partition) + app-level orchestration | No cross-partition transactions in Cosmos DB. |
| Change Feed | N/A (triggers, CDC) | `ChangeFeedProcessor` (Java SDK) | Hosts CP-1 through CP-5. Can run in-process or Azure Functions. |
| Caching | Spring Cache / Redis | Spring Cache / Redis (unchanged) + Cosmos `_etag` for conditional reads | Reference data (`holdings-reference`) should use `@Cacheable`. |

### 13.2 Maven Dependencies

```xml
<properties>
    <java.version>17</java.version>
    <spring-boot.version>3.2.1</spring-boot.version>
    <azure-spring-data-cosmos.version>5.19.0</azure-spring-data-cosmos.version>
    <azure-cosmos.version>4.65.0</azure-cosmos.version>
</properties>

<dependencies>
    <!-- Spring Data Azure Cosmos — provides CosmosRepository, @Container, etc. -->
    <dependency>
        <groupId>com.azure</groupId>
        <artifactId>azure-spring-data-cosmos</artifactId>
        <version>${azure-spring-data-cosmos.version}</version>
    </dependency>

    <!-- Azure Cosmos DB Java SDK v4 — for Change Feed, Bulk, Batch, advanced queries -->
    <dependency>
        <groupId>com.azure</groupId>
        <artifactId>azure-cosmos</artifactId>
        <version>${azure-cosmos.version}</version>
    </dependency>

    <!-- Azure Identity — for DefaultAzureCredential (recommended over keys) -->
    <dependency>
        <groupId>com.azure</groupId>
        <artifactId>azure-identity</artifactId>
        <version>1.14.2</version>
    </dependency>

    <!-- Spring Boot Actuator — for health checks and metrics -->
    <dependency>
        <groupId>org.springframework.boot</groupId>
        <artifactId>spring-boot-starter-actuator</artifactId>
    </dependency>

    <!-- Micrometer for Cosmos DB metrics (optional but recommended) -->
    <dependency>
        <groupId>io.micrometer</groupId>
        <artifactId>micrometer-registry-prometheus</artifactId>
    </dependency>
</dependencies>
```

### 13.3 Application Configuration

```yaml
# application.yml
azure:
  cosmos:
    endpoint: ${AZURE_COSMOS_ENDPOINT}
    # key: ${AZURE_COSMOS_KEY}  # Prefer DefaultAzureCredential over keys
    database: holdings-db
    populate-query-metrics: true
    consistency-level: SESSION

spring:
  cloud:
    azure:
      credential:
        managed-identity-enabled: true  # Use Managed Identity in Azure
      cosmos:
        endpoint: ${AZURE_COSMOS_ENDPOINT}
        database: holdings-db
```

### 13.4 Cosmos DB Configuration Class

> Follows **`sdk-java-cosmos-config`** — uses dependent `@Bean` chain, never `@PostConstruct`.

```java
@Configuration
@EnableCosmosRepositories(basePackages = "com.temenos.holdings.repository")
public class CosmosConfig extends AbstractCosmosConfiguration {

    @Value("${azure.cosmos.endpoint}")
    private String endpoint;

    @Value("${azure.cosmos.database}")
    private String database;

    /**
     * Singleton CosmosClient bean — sdk-singleton-client.
     * Uses DefaultAzureCredential for passwordless auth (recommended).
     * Falls back to Gateway mode for local emulator.
     */
    @Bean(destroyMethod = "close")
    public CosmosClientBuilder cosmosClientBuilder() {
        DirectConnectionConfig directConfig = DirectConnectionConfig.getDefaultConfig();
        directConfig.setConnectTimeout(Duration.ofSeconds(5));
        directConfig.setIdleConnectionTimeout(Duration.ofSeconds(60));

        CosmosClientBuilder builder = new CosmosClientBuilder()
            .endpoint(endpoint)
            .credential(new DefaultAzureCredentialBuilder().build())
            .consistencyLevel(ConsistencyLevel.SESSION)
            .contentResponseOnWriteEnabled(true)   // sdk-java-content-response
            .directMode(directConfig);             // sdk-connection-mode: Direct for production

        return builder;
    }

    @Override
    protected String getDatabaseName() {
        return database;
    }

    /**
     * Low-level CosmosAsyncClient for Change Feed processors and Bulk operations.
     * Reuses the same builder config as Spring Data.
     */
    @Bean(destroyMethod = "close")
    public CosmosAsyncClient cosmosAsyncClient(CosmosClientBuilder builder) {
        return builder.buildAsyncClient();
    }

    /**
     * Container references for direct SDK access (Change Feed, Batch).
     * Spring Data handles its own container access via repositories.
     */
    @Bean
    public CosmosAsyncDatabase cosmosAsyncDatabase(CosmosAsyncClient client) {
        return client.getDatabase(database);
    }

    @Bean("arrangementsContainer")
    public CosmosAsyncContainer arrangementsContainer(CosmosAsyncDatabase db) {
        return db.getContainer("holdings-arrangements");
    }

    @Bean("partiesContainer")
    public CosmosAsyncContainer partiesContainer(CosmosAsyncDatabase db) {
        return db.getContainer("holdings-parties");
    }

    @Bean("altKeysContainer")
    public CosmosAsyncContainer altKeysContainer(CosmosAsyncDatabase db) {
        return db.getContainer("holdings-alt-keys");
    }
}
```

### 13.5 Entity Model Examples

> Follows **`sdk-spring-data-annotations`** — replaces all JPA annotations.

#### Arrangement Entity

```java
@Container(containerName = "holdings-arrangements")
@Data  // Lombok
@NoArgsConstructor
public class Arrangement {

    @Id
    private String id;

    @PartitionKey
    private String arrangementId;

    private String type = "arrangement";

    private String businessKey;
    private String productGroup;
    private String productId;
    private String productLine;
    private String status;
    private String currency;
    private String linkedReference;
    private String startDate;
    private String maturityDate;

    // Embedded objects (formerly @Embeddable children)
    private AccountArr accountArr;
    private LendingArr lendingArr;
    private DepositArr depositArr;
    private ArrangementInterest arrangementInterest;
    private ScheduleDetails scheduleDetails;
    private ProductDetails productDetails;
    private OfficerDetails officerDetails;
    private CompanyDetails companyDetails;

    // Embedded arrays (bounded)
    private List<PostingRestrictArrangement> postingRestrictions;
    private List<EmailAddressType> emailAddresses;
    private List<CorrespondenceAddress> correspondenceAddresses;
    private List<AlternateKey> alternateKeys;
    // NOTE: ArrangementBills NOT embedded — separate arrangementBill documents
}
```

#### PartyArrangement (Multi-doc in same container)

```java
@Container(containerName = "holdings-arrangements")
@Data
@NoArgsConstructor
public class PartyArrangement {

    @Id
    private String id;  // "PA-{arrangementId}-{partyId}"

    @PartitionKey
    private String arrangementId;

    private String type = "partyArrangement";

    private String partyId;
    private String partyRole;
    private boolean isPartyOwner;
    private boolean isActive;

    // Denormalized from PartyDetails (propagated by CP-5)
    private PartySummary partySummary;

    // Denormalized arrangement summary (propagated by CP-2)
    private ArrangementSummary arrangementSummary;
}

@Data
@NoArgsConstructor
public class PartySummary {
    private String partyName;
    private String firstName;
    private String lastName;
    private String customerSegment;
    private String nationality;
}

@Data
@NoArgsConstructor
public class ArrangementSummary {
    private String productGroup;
    private String productId;
    private String productLine;
    private String status;
    private String arrangementStatus;
    private String currency;
    private String linkedReference;
    private String accountCategory;
    private String startDate;
    private String extArrangementId;
}
```

#### ArrangementBill (Separate documents in same container)

```java
@Container(containerName = "holdings-arrangements")
@Data
@NoArgsConstructor
public class ArrangementBill {

    @Id
    private String id;  // "BILL-{arrangementId}-{billDate}-{billType}"

    @PartitionKey
    private String arrangementId;

    private String type = "arrangementBill";

    private String billDate;
    private String billType;
    private String billStatus;
    private String deferredSettlement;
    private BigDecimal billAmount;
    private String currency;
    private List<BillTypeDetails> billTypeDetails;
}
```

#### Balance Entity (Separate container)

```java
@Container(containerName = "holdings-balances")
@Data
@NoArgsConstructor
public class Balance {

    @Id
    private String id;  // "BAL-{accountId}-{balanceType}"

    @PartitionKey
    private String accountId;

    private String type = "balance";
    private String businessKey;
    private String balanceType;
    private BigDecimal amount;
    private String currency;
    private List<LockedAmount> lockedAmounts;
    private BigDecimal availableBalance;
    private Instant lastUpdated;
}
```

### 13.6 Repository Interfaces

> Follows **`sdk-spring-data-repository`** — uses `CosmosRepository<T, String>`, `Iterable` return types.

```java
@Repository
public interface ArrangementRepository extends CosmosRepository<Arrangement, String> {

    // AP-4: Get Arrangement by ID (single-partition, type = 'arrangement')
    // Spring Data auto-generates single-partition query
    List<Arrangement> findByArrangementIdAndType(String arrangementId, String type);
}

@Repository
public interface PartyArrangementRepository extends CosmosRepository<PartyArrangement, String> {

    // Used by AP-4: Get all partyArrangement docs for an arrangement
    List<PartyArrangement> findByArrangementIdAndType(String arrangementId, String type);
}

@Repository
public interface BalanceRepository extends CosmosRepository<Balance, String> {

    // AP-1: Get Balance by accountId
    List<Balance> findByAccountId(String accountId);
}

@Repository
public interface PartyArrangementByPartyRepository
        extends CosmosRepository<PartyArrangement, String> {

    // AP-5 (list mode): Get arrangements for a party
    // NOTE: This queries holdings-parties container (materialized view)
    // Requires separate @Container annotation pointing to holdings-parties
    @Query("SELECT * FROM c WHERE c.partyId = @partyId AND c.type = 'partyArrangement'")
    List<PartyArrangement> findByPartyIdAndType(
        @Param("partyId") String partyId,
        @Param("type") String type);
}

@Repository
public interface TransactionRepository extends CosmosRepository<Transaction, String> {

    // AP-2: Get transactions by accountId with filters (paginated)
    @Query("SELECT * FROM c WHERE c.accountId = @acctId AND c.processingDate >= @fromDate "
         + "ORDER BY c.processingDate DESC")
    List<Transaction> findByAccountIdAndDateRange(
        @Param("acctId") String accountId,
        @Param("fromDate") String fromDate);
}

@Repository
public interface ArrangementBillRepository extends CosmosRepository<ArrangementBill, String> {

    // AP-10: Get bills by arrangement (paginated)
    @Query("SELECT * FROM c WHERE c.arrangementId = @arrId AND c.type = 'arrangementBill' "
         + "ORDER BY c.billDate DESC")
    List<ArrangementBill> findBillsByArrangementId(
        @Param("arrId") String arrangementId);
}
```

### 13.7 Service Layer — Multi-Container Access Patterns

> For access patterns requiring cross-container reads (AP-3, AP-5 full-detail, AP-6, AP-7, AP-9), use the low-level Java SDK directly via injected `CosmosAsyncContainer` beans. Spring Data repositories handle single-container patterns.

```java
@Service
@RequiredArgsConstructor
public class ArrangementService {

    private final ArrangementRepository arrangementRepo;
    private final PartyArrangementRepository partyArrangementRepo;

    @Qualifier("arrangementsContainer")
    private final CosmosAsyncContainer arrangementsContainer;

    @Qualifier("altKeysContainer")
    private final CosmosAsyncContainer altKeysContainer;

    @Qualifier("partiesContainer")
    private final CosmosAsyncContainer partiesContainer;

    /**
     * AP-3: Get Account Details by accountId.
     * Step 1: Resolve accountId → arrangementId via alt-keys (1 RU point read).
     * Step 2: Query all docs for arrangement (5 RU single-partition query).
     */
    public Mono<AccountDetailsResponse> getAccountDetails(String accountId) {
        // Step 1: Point read from alt-keys container
        return altKeysContainer
            .readItem(accountId, new PartitionKey(accountId), AltKey.class)
            .map(response -> response.getItem().getArrangementId())
            // Step 2: Query arrangements container
            .flatMap(arrId -> {
                SqlQuerySpec query = new SqlQuerySpec(
                    "SELECT * FROM c WHERE c.arrangementId = @arrId "
                  + "AND c.type != 'arrangementBill'",
                    List.of(new SqlParameter("@arrId", arrId)));
                CosmosQueryRequestOptions opts = new CosmosQueryRequestOptions()
                    .setPartitionKey(new PartitionKey(arrId));

                return arrangementsContainer
                    .queryItems(query, opts, JsonNode.class)
                    .byPage()
                    .flatMapIterable(FeedResponse::getResults)
                    .collectList();
            })
            .map(AccountDetailsResponse::fromDocuments);
    }

    /**
     * AP-4: Get Arrangement by arrangementId — single container, single partition.
     * Uses Spring Data repository for simplicity.
     */
    public ArrangementDetailsResponse getArrangement(String arrangementId) {
        List<Arrangement> arrangements = arrangementRepo
            .findByArrangementIdAndType(arrangementId, "arrangement");
        List<PartyArrangement> parties = partyArrangementRepo
            .findByArrangementIdAndType(arrangementId, "partyArrangement");
        return ArrangementDetailsResponse.from(arrangements.get(0), parties);
    }

    /**
     * AP-9: Bulk get arrangements — parallelized single-partition queries.
     * Uses low-level SDK for Flux.merge() parallel execution.
     */
    public Flux<List<JsonNode>> getBulkArrangements(List<String> arrangementIds) {
        List<Mono<List<JsonNode>>> queries = arrangementIds.stream()
            .map(arrId -> {
                SqlQuerySpec query = new SqlQuerySpec(
                    "SELECT * FROM c WHERE c.arrangementId = @arrId "
                  + "AND c.type != 'arrangementBill'",
                    List.of(new SqlParameter("@arrId", arrId)));
                CosmosQueryRequestOptions opts = new CosmosQueryRequestOptions()
                    .setPartitionKey(new PartitionKey(arrId));

                return arrangementsContainer
                    .queryItems(query, opts, JsonNode.class)
                    .byPage()
                    .flatMapIterable(FeedResponse::getResults)
                    .collectList();
            })
            .toList();

        return Flux.merge(queries);  // Parallel execution
    }
}
```

### 13.8 Change Feed Processors

> The 5 compensating patterns (CP-1 through CP-5) use the Java SDK's `ChangeFeedProcessor`. These can run as Spring-managed beans with lifecycle hooks, or be hosted in Azure Functions with Cosmos DB trigger.

```java
@Component
@RequiredArgsConstructor
@Slf4j
public class ChangeFeedProcessorConfig {

    @Qualifier("arrangementsContainer")
    private final CosmosAsyncContainer arrangementsContainer;

    @Qualifier("partiesContainer")
    private final CosmosAsyncContainer partiesContainer;

    @Qualifier("altKeysContainer")
    private final CosmosAsyncContainer altKeysContainer;

    private final CosmosAsyncDatabase database;

    private ChangeFeedProcessor partyArrangementSyncProcessor;
    private ChangeFeedProcessor partySummarySyncProcessor;

    /**
     * CP-1: Sync PartyArrangement → holdings-parties (materialized view).
     * CP-2: Sync ArrangementSummary → holdings-parties (enriched summary).
     * CP-3: Sync AltKey Lookup.
     * Source: Change Feed on holdings-arrangements.
     */
    @Bean
    public ChangeFeedProcessor arrangementChangeFeedProcessor() {
        // Lease container for checkpoint management
        CosmosAsyncContainer leaseContainer = database.getContainer("holdings-leases");

        return new ChangeFeedProcessorBuilder()
            .hostName("holdings-service-" + UUID.randomUUID())
            .feedContainer(arrangementsContainer)
            .leaseContainer(leaseContainer)
            .handleChanges((List<JsonNode> docs, ChangeFeedProcessorContext ctx) -> {
                for (JsonNode doc : docs) {
                    String type = doc.path("type").asText();
                    switch (type) {
                        case "partyArrangement" -> handlePartyArrangementSync(doc);    // CP-1
                        case "arrangement"      -> handleArrangementSummarySync(doc);  // CP-2
                        default -> { /* no-op for other types */ }
                    }
                    // CP-3: AltKey sync for all doc types with alternateKeys
                    if (doc.has("alternateKeys")) {
                        handleAltKeySync(doc);
                    }
                }
                return Mono.empty();
            })
            .buildChangeFeedProcessor();
    }

    /**
     * CP-5: Sync PartyDetails → partyArrangement.partySummary in arrangements.
     * Source: Change Feed on holdings-parties.
     */
    @Bean
    public ChangeFeedProcessor partyChangeFeedProcessor() {
        CosmosAsyncContainer leaseContainer = database.getContainer("holdings-leases");

        return new ChangeFeedProcessorBuilder()
            .hostName("holdings-party-sync-" + UUID.randomUUID())
            .feedContainer(partiesContainer)
            .leaseContainer(leaseContainer)
            .handleChanges((List<JsonNode> docs, ChangeFeedProcessorContext ctx) -> {
                for (JsonNode doc : docs) {
                    if ("partyDetails".equals(doc.path("type").asText())) {
                        handlePartySummarySync(doc);  // CP-5
                    }
                }
                return Mono.empty();
            })
            .buildChangeFeedProcessor();
    }

    /** CP-1: Upsert partyArrangement into holdings-parties */
    private void handlePartyArrangementSync(JsonNode doc) {
        String partyId = doc.path("partyId").asText();
        partiesContainer.upsertItem(doc, new PartitionKey(partyId),
            new CosmosItemRequestOptions())
            .doOnError(e -> log.error("CP-1 failed for {}", doc.path("id"), e))
            .subscribe();
    }

    /** CP-2: Patch arrangementSummary in all linked partyArrangement docs */
    private void handleArrangementSummarySync(JsonNode doc) {
        String arrId = doc.path("arrangementId").asText();
        ArrangementSummary summary = buildEnrichedSummary(doc);

        // Find all partyArrangement docs in parties container linked to this arrangement
        // Then patch each with updated summary
        CosmosPatchOperations patchOps = CosmosPatchOperations.create()
            .replace("/arrangementSummary", summary);

        // Query partyArrangements in arrangements container to find linked partyIds
        // Then patch the materialized copies in parties container
        arrangementsContainer.queryItems(
            new SqlQuerySpec("SELECT c.id, c.partyId FROM c WHERE c.arrangementId = @arrId AND c.type = 'partyArrangement'",
                List.of(new SqlParameter("@arrId", arrId))),
            new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(arrId)),
            JsonNode.class)
            .byPage()
            .flatMapIterable(FeedResponse::getResults)
            .flatMap(pa -> partiesContainer.patchItem(
                pa.path("id").asText(),
                new PartitionKey(pa.path("partyId").asText()),
                patchOps, PartyArrangement.class))
            .doOnError(e -> log.error("CP-2 failed for arrangement {}", arrId, e))
            .subscribe();
    }

    /** CP-5: Patch partySummary in all linked partyArrangement docs in arrangements */
    private void handlePartySummarySync(JsonNode doc) {
        String partyId = doc.path("partyId").asText();
        PartySummary summary = new PartySummary();
        summary.setPartyName(doc.path("firstName").asText() + " " + doc.path("lastName").asText());
        summary.setFirstName(doc.path("firstName").asText());
        summary.setLastName(doc.path("lastName").asText());
        summary.setCustomerSegment(doc.path("customerSegment").asText());
        summary.setNationality(doc.path("nationality").asText());

        CosmosPatchOperations patchOps = CosmosPatchOperations.create()
            .replace("/partySummary", summary);

        // Find all partyArrangement docs for this party (from parties container)
        partiesContainer.queryItems(
            new SqlQuerySpec("SELECT c.arrangementId, c.id FROM c WHERE c.partyId = @pId AND c.type = 'partyArrangement'",
                List.of(new SqlParameter("@pId", partyId))),
            new CosmosQueryRequestOptions().setPartitionKey(new PartitionKey(partyId)),
            JsonNode.class)
            .byPage()
            .flatMapIterable(FeedResponse::getResults)
            .flatMap(pa -> arrangementsContainer.patchItem(
                pa.path("id").asText(),
                new PartitionKey(pa.path("arrangementId").asText()),
                patchOps, PartyArrangement.class))
            .doOnError(e -> log.error("CP-5 failed for party {}", partyId, e))
            .subscribe();
    }

    /** Start/stop lifecycle management */
    @PostConstruct
    public void start() {
        // Note: @PostConstruct is OK here — we're NOT calling @Bean methods
        log.info("Starting Change Feed processors...");
    }
}
```

### 13.9 Transactional Batch (Same-Partition Multi-Doc Writes)

> Replaces JPA `@Transactional` for co-located writes (e.g., creating arrangement + partyArrangement + schedules atomically).

```java
@Service
@RequiredArgsConstructor
public class ArrangementWriteService {

    @Qualifier("arrangementsContainer")
    private final CosmosAsyncContainer arrangementsContainer;

    /**
     * WP-1 + WP-4: Create arrangement with party associations (atomic, same partition).
     * Uses CosmosBatch for transactional guarantee within a single partition.
     */
    public Mono<CosmosBatchResponse> createArrangementWithParties(
            Arrangement arrangement,
            List<PartyArrangement> parties,
            List<PaymentSchedule> schedules) {

        CosmosBatch batch = CosmosBatch.createCosmosBatch(
            new PartitionKey(arrangement.getArrangementId()));

        batch.createItemOperation(arrangement);
        parties.forEach(pa -> batch.createItemOperation(pa));
        schedules.forEach(sched -> batch.createItemOperation(sched));

        return arrangementsContainer.executeCosmosBatch(batch)
            .doOnSuccess(response -> {
                if (!response.isSuccessStatusCode()) {
                    log.error("Batch failed: status={}", response.getStatusCode());
                }
            });
    }
}
```

### 13.10 Key Migration Patterns: JPA → Spring Data Cosmos

| JPA Pattern | Spring Data Cosmos Replacement | Notes |
|:-----------|:------------------------------|:------|
| `@Entity` | `@Container(containerName = "...")` | Maps to Cosmos container, not table |
| `@Table(name = "ms_holdings_*")` | `@Container(containerName = "holdings-*")` | Container names, not table names |
| `@Column(name = "...")` | Remove — field names are JSON property names | Use `@JsonProperty` for custom serialization |
| `@Id` + `@GeneratedValue(strategy = IDENTITY)` | `@Id` (`org.springframework.data.annotation.Id`) + `@GeneratedValue` | Must be `String` type |
| `@ManyToOne` / `@OneToMany` / `@JoinColumn` | Remove — use embedded objects or ID references | See entity models above |
| `@Embeddable` / `@Embedded` | Plain Java class (nested object) | Jackson serializes as nested JSON |
| `JpaRepository<T, Long>` | `CosmosRepository<T, String>` | ID type changes from `Long`→`String` |
| `Page<T>` / `Pageable` | `CosmosPagedFlux<T>` / continuation tokens | No offset-based pagination in Cosmos |
| `@Query("SELECT p FROM Entity p WHERE ...")` (JPQL) | `@Query("SELECT * FROM c WHERE ...")` (SQL) | Cosmos SQL, not JPQL |
| `EntityManager.persist()` | `repository.save()` or `container.createItem()` | Spring Data or direct SDK |
| `@Transactional` | `CosmosBatch` (same partition) | No cross-partition transactions |
| `CascadeType.ALL` | App-level: `CosmosBatch` or Change Feed | Explicit cascade management |
| `FetchType.LAZY` | Not applicable — design doc model upfront | Cosmos returns full document; control via query projection `SELECT c.field1, c.field2` |
| `@Version` (optimistic locking) | `_etag` + `CosmosItemRequestOptions.setIfMatchETag()` | Built-in optimistic concurrency |
| `spring.datasource.*` | `azure.cosmos.*` | Different config namespace |

### 13.11 Spring Boot Best Practices for Cosmos DB

| # | Practice | Rule Reference | Detail |
|--:|:---------|:-------------|:-------|
| 1 | **Singleton `CosmosClient`** | `sdk-singleton-client` | Never create per-request. Use `@Bean(destroyMethod = "close")`. Spring manages lifecycle. |
| 2 | **Direct connection mode** | `sdk-connection-mode` | Use `directMode()` in production. `gatewayMode()` only for emulator/local dev. |
| 3 | **Enable content response on writes** | `sdk-java-content-response` | Set `contentResponseOnWriteEnabled(true)` on builder. Otherwise `getItem()` returns null after create/upsert. |
| 4 | **Use `@Bean` chain, not `@PostConstruct`** | `sdk-java-cosmos-config` | Dependent `@Bean` methods with parameter injection. `@PostConstruct` calling `@Bean` causes circular dependency. |
| 5 | **String IDs only** | `sdk-spring-data-annotations` | `CosmosRepository<T, String>`. Never `Integer`/`Long` IDs with Cosmos. |
| 6 | **Handle `Iterable` returns** | `sdk-spring-data-repository` | `CosmosRepository.findAll()` returns `Iterable`, not `List`. Use `StreamSupport.stream()` for conversion. |
| 7 | **Prefer `DefaultAzureCredential`** | IAM best practice | Passwordless auth via Managed Identity in Azure, Azure CLI locally. Avoid key-based auth in production. |
| 8 | **Configure preferred regions** | `sdk-preferred-regions` | `builder.preferredRegions(List.of("UK South", "UK West"))` — order by proximity. |
| 9 | **Log diagnostics on errors** | `sdk-diagnostics` | `response.getDiagnostics()` — log on 429s, timeouts, slow queries (>10 RU). |
| 10 | **Use reactive for fan-out patterns** | `sdk-async-api` | AP-5 full-detail, AP-7, AP-9 use `Flux.merge()` for parallel reads across arrangements. Spring WebFlux optional but recommended for reactive chains. |
| 11 | **Serialize enums as strings** | `sdk-serialization-enums` | Configure Jackson: `objectMapper.configure(SerializationFeature.WRITE_ENUMS_USING_TO_STRING, true)`. Avoids integer enum values in Cosmos docs. |
| 12 | **Cache reference data** | `@Cacheable` | `holdings-reference` container data (country codes, party roles, transaction types) should be cached with 5-minute TTL. Zero RU cost after first load. |
| 13 | **Health check** | Spring Actuator | Implement custom `HealthIndicator` that calls `cosmosClient.readAllDatabases()` for `/actuator/health`. |
| 14 | **Metrics export** | Micrometer | Export RU consumption, latency P50/P95/P99, and 429 rates to Prometheus/App Insights via Micrometer. Cosmos Java SDK supports `CosmosMicrometerMetricsOptions`. |

### 13.12 Change Feed Hosting Recommendations

| Option | Pros | Cons | Recommended For |
|:-------|:-----|:-----|:----------------|
| **In-process Spring Boot service** | Simplest deployment, shared config, `@Bean` lifecycle | Tied to app scaling, restart interrupts processing | CP-1 through CP-5 at current scale (low TPS) |
| **Dedicated Spring Boot worker** | Independent scaling, no impact on API latency | Separate deployment artifact | If Change Feed load exceeds 10% of app resources |
| **Azure Functions (Cosmos DB trigger)** | Serverless scaling, built-in lease management, per-invocation billing | Cold start latency, limited runtime control, Java startup slower | High-volume CP patterns (>1,000 TPS) or cost-optimized low-frequency feeds |

**Recommendation at current scale:** In-process `ChangeFeedProcessor` beans within the Holdings Spring Boot service. The 5 compensating patterns generate ~1,750 RU/s total — well within a shared deployment. Migrate to dedicated worker or Azure Functions only if Change Feed processing causes API latency degradation.

### 13.13 Open Items — Spring Boot Migration

| # | Item | Priority | Owner |
|--:|:-----|:---------|:------|
| 1 | Create Spring Data Cosmos entity classes for all 9 containers | P0 | Dev team |
| 2 | Replace all `@Entity`/`@Table` annotations with `@Container`/`@PartitionKey` | P0 | Dev team |
| 3 | Migrate all `JpaRepository` interfaces to `CosmosRepository` | P0 | Dev team |
| 4 | Remove Hibernate/JPA dependencies from `pom.xml` | P0 | Dev team |
| 5 | Implement `ChangeFeedProcessor` beans for CP-1 through CP-5 | P1 | Dev team |
| 6 | Set up `DefaultAzureCredential` for local dev (Azure CLI) and production (Managed Identity) | P1 | DevOps |
| 7 | Configure Micrometer metrics export for Cosmos DB RU tracking | P1 | SRE |
| 8 | Implement custom Spring Actuator `HealthIndicator` for Cosmos DB connectivity | P1 | Dev team |
| 9 | Add integration tests with Cosmos DB Emulator (Docker) and `@TestConfiguration` | P1 | Dev team |
| 10 | Evaluate Spring WebFlux vs. Spring MVC for reactive Cosmos operations | P2 | Architecture |

---

## Appendix A: Assumptions & Open Items

### Assumptions Made (to be validated with DBA/Dev team)

| # | Assumption | Impact if Wrong | Mitigation |
|--:|:-----------|:---------------|:-----------|
| 1 | `accountId` maps to `arrangementId` for account-type arrangements (resolvable via MsAltKey) | AP-1/AP-2 would need additional lookup step | Alt-key lookup container handles this; already priced in |
| 2 | `contractId` in BusinessContractActivity/Balance = `arrangementId` | Contract data would need separate container with different PK | Move to separate container if PK differs; add HPK `/systemId`/`/companyId`/`/contractId` |
| 3 | Average 2 parties per arrangement, max ~10 | Fan-out RU estimates for AP-5/AP-7 would change | Monitor and adjust autoscale. Consider richer materialized view if avg > 5. |
| 4 | Row counts for tables without explicit volumes (marked as guesstimate) | Storage and RU estimates could be off by 2-5× | Run actual `COUNT(*)` and `AVG(DATALENGTH(*))` before provisioning |
| 4a | ⚠️ **INVALIDATED:** Arrangement document size was estimated at 2 KB | **Real sample is 187 KB (93× larger) due to unbounded `arrangementBills` array**. Storage estimate was wrong by 7.5×. | **RESOLVED by extracting bills to separate docs. Core arrangement now ~3 KB (validated from real sample). Bill doc ~0.4 KB each.** |
| 5 | Write TPS inferred from domain patterns (no explicit write metrics provided) | Could under/over-provision write RU | Instrument write metrics in current system before migration |
| 6 | PaymentOrder and PaymentTransaction are keyed by accountId (debit account) | Would need different PK if primarily queried by paymentOrderId | Add paymentOrderId to alt-keys container if needed |

### Open Items

| # | Item | Owner | Priority |
|--:|:-----|:------|:---------|
| 1 | Validate actual row counts and data sizes for all 34 tables | DBA | P0 — before provisioning |
| 2 | Confirm accountId ↔ arrangementId mapping mechanism | Dev team | P0 — affects core design |
| 3 | Define multi-region requirements (active-active, consistency) | Architecture | P1 |
| 4 | Define data retention/archival policies per entity type | Business | P1 |
| 5 | Instrument current system for actual TPS per API endpoint | SRE | P1 |
| 6 | Define monitoring/alerting thresholds for Cosmos DB metrics | SRE | P1 |
| 7 | Evaluate Synapse Link requirement for analytics/reporting workloads | Data team | P2 |
| 8 | Review Change Feed processor hosting (Azure Functions vs dedicated service) | Dev team | P2 |
| 9 | **⚠️ NEW:** Collect more sample documents across arrangement types (Lending, Deposit, Portfolio) to validate bill growth rates and doc sizes | DBA/Dev team | P0 |
| 10 | **⚠️ NEW:** Evaluate TTL + archival strategy for bill documents (>3 years → cold storage) to manage 2.9 TB arrangements container | Architecture/Business | P1 |
| 11 | **⚠️ NEW:** Evaluate separate `holdings-bills` container vs keeping bills in `holdings-arrangements` (storage-dominant partition provisioning trade-off) | Architecture | P1 |

---

## Appendix B: RDBMS Construct Mapping

| RDBMS Construct | Source Example | Cosmos DB Equivalent | Notes |
|:---------------|:-------------|:--------------------|:------|
| JOIN (FK) — 1:1/1:Few bounded | Arrangement → embeddable children | Embedded in single document | 14 @Embeddable children → embedded objects/arrays |
| JOIN (FK) — 1:N bounded | Arrangement → PaymentSchedules | Multi-doc in same container with `type` discriminator | Query: `WHERE arrangementId = @id AND type = 'paymentSchedule'` |
| JOIN (Junction/M:N) | Arrangement ↔ PartyArrangement ↔ PartyDetails | Dual-doc pattern: co-located + materialized view | Change Feed propagation maintains consistency |
| AUTO_INCREMENT | `recId IDENTITY` on PartyArrangement | Natural composite key: `PA-{arrangementId}-{partyId}` | Deterministic, idempotent |
| UNIQUE constraint | `businessKey` on various tables | Unique Key Policy on container + alt-key lookup | `uniqueKeyPolicy: { uniqueKeys: [{ paths: ["/businessKey"] }] }` |
| Composite index | `(processingDate, transactionAmount)` on Transaction | Composite index in indexing policy | See Section 3.3 indexing policies |
| Foreign key enforcement | `PartyArrangement.arrangementId → Arrangement.arrangementId` | Application-level validation | No server-side FK enforcement in Cosmos DB |
| Stored procedure | Multi-table transaction logic | App-layer logic or Cosmos DB stored proc (same partition) | `CosmosBatch` (Java SDK) for same-partition multi-doc writes. Or `@Transactional` in Spring service layer for app-orchestrated transactions. |
| Trigger (AFTER INSERT) | Cascading updates, audit logging | Change Feed processor (`ChangeFeedProcessor`) | 5 processors identified (CP-1 through CP-5). Hosted in Spring Boot via `@Bean` lifecycle. |
| Default values | `DEFAULT GETDATE()` | SDK serialization defaults / application layer | `_ts` set automatically by Cosmos; app sets business dates via `@PrePersist`-style constructors or Jackson defaults. |
| CHECK constraint | Business validation rules | Application-level validation before write | Validate in Spring service layer (`@Valid`, Bean Validation) |
| CASCADE DELETE | `ON DELETE CASCADE` on child tables | Transactional batch (`CosmosBatch`) same partition or application-level | ArrangementId-scoped deletes can use `CosmosBatch` for atomic multi-doc delete within partition |
