using System.Diagnostics;
using System.Globalization;
using System.Net;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

// ═══════════════════════════════════════════════════════════════════════
// AdventureWorks SQL Server → Cosmos DB Data Migration
// ═══════════════════════════════════════════════════════════════════════
// CSV source files are BCP exports with NO headers.
// Two delimiter formats:
//   Tab-delimited:  fields separated by \t
//   Pipe-delimited: fields separated by +|  rows terminated by &|
// ═══════════════════════════════════════════════════════════════════════

const string endpoint = "https://ssm-cosmos-adventureworks01.documents.azure.com:443/";
const string databaseName = "adventureworks";
const string productsContainerName = "products";
const string customersContainerName = "customers";

// Resolve schema/ folder relative to the project
var schemaDir = FindSchemaDir();
Console.WriteLine($"Schema folder: {schemaDir}");

// ─── Build Cosmos client with bulk enabled ───────────────────────────
var cosmosClient = new CosmosClient(endpoint, new DefaultAzureCredential(),
    new CosmosClientOptions
    {
        AllowBulkExecution = true,
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });

var db = cosmosClient.GetDatabase(databaseName);
var productsContainer = db.GetContainer(productsContainerName);
var customersContainer = db.GetContainer(customersContainerName);

Console.WriteLine("Connected to Cosmos DB.");

var sw = Stopwatch.StartNew();

// ═══════════════════════════════════════════════════════════════════════
// STEP 1 — Read all CSV files into lookup dictionaries
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("\n=== STEP 1: Reading CSV files ===");

// Tab-delimited files
var productCategoryRows = ReadTabFile(Path.Combine(schemaDir, "ProductCategory.csv"));
Console.WriteLine($"  ProductCategory: {productCategoryRows.Count} rows");

var productSubcategoryRows = ReadTabFile(Path.Combine(schemaDir, "ProductSubcategory.csv"));
Console.WriteLine($"  ProductSubcategory: {productSubcategoryRows.Count} rows");

var productRows = ReadTabFile(Path.Combine(schemaDir, "Product.csv"));
Console.WriteLine($"  Product: {productRows.Count} rows");

var productDescriptionRows = ReadTabFile(Path.Combine(schemaDir, "ProductDescription.csv"));
Console.WriteLine($"  ProductDescription: {productDescriptionRows.Count} rows");

var pmpdcRows = ReadTabFile(Path.Combine(schemaDir, "ProductModelProductDescriptionCulture.csv"));
Console.WriteLine($"  PMPDC: {pmpdcRows.Count} rows");

var customerRows = ReadTabFile(Path.Combine(schemaDir, "Customer.csv"));
Console.WriteLine($"  Customer: {customerRows.Count} rows");

var addressRows = ReadTabFile(Path.Combine(schemaDir, "Address.csv"));
Console.WriteLine($"  Address: {addressRows.Count} rows");

var salesOrderHeaderRows = ReadTabFile(Path.Combine(schemaDir, "SalesOrderHeader.csv"));
Console.WriteLine($"  SalesOrderHeader: {salesOrderHeaderRows.Count} rows");

var salesOrderDetailRows = ReadTabFile(Path.Combine(schemaDir, "SalesOrderDetail.csv"));
Console.WriteLine($"  SalesOrderDetail: {salesOrderDetailRows.Count} rows");

var shipMethodRows = ReadTabFile(Path.Combine(schemaDir, "ShipMethod.csv"));
Console.WriteLine($"  ShipMethod: {shipMethodRows.Count} rows");

var stateProvinceRows = ReadTabFile(Path.Combine(schemaDir, "StateProvince.csv"));
Console.WriteLine($"  StateProvince: {stateProvinceRows.Count} rows");

// Pipe-delimited files
var productModelRows = ReadPipeFile(Path.Combine(schemaDir, "ProductModel.csv"));
Console.WriteLine($"  ProductModel: {productModelRows.Count} rows");

var personRows = ReadPipeFile(Path.Combine(schemaDir, "Person.csv"));
Console.WriteLine($"  Person: {personRows.Count} rows");

var emailAddressRows = ReadPipeFile(Path.Combine(schemaDir, "EmailAddress.csv"));
Console.WriteLine($"  EmailAddress: {emailAddressRows.Count} rows");

var passwordRows = ReadPipeFile(Path.Combine(schemaDir, "Password.csv"));
Console.WriteLine($"  Password: {passwordRows.Count} rows");

var personPhoneRows = ReadPipeFile(Path.Combine(schemaDir, "PersonPhone.csv"));
Console.WriteLine($"  PersonPhone: {personPhoneRows.Count} rows");

var businessEntityAddressRows = ReadPipeFile(Path.Combine(schemaDir, "BusinessEntityAddress.csv"));
Console.WriteLine($"  BusinessEntityAddress: {businessEntityAddressRows.Count} rows");

// Tab-delimited lookup for address type resolution
var addressTypeRows = ReadTabFile(Path.Combine(schemaDir, "AddressType.csv"));
Console.WriteLine($"  AddressType: {addressTypeRows.Count} rows");

// ═══════════════════════════════════════════════════════════════════════
// STEP 2 — Build in-memory lookup dictionaries
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("\n=== STEP 2: Building lookup dictionaries ===");

// Parent categories: ProductCategoryID → Name
// Columns: ProductCategoryID(0), Name(1), rowguid(2), ModifiedDate(3)
var parentCategoryLookup = productCategoryRows
    .ToDictionary(r => r[0].Trim(), r => r[1].Trim());
Console.WriteLine($"  Parent categories: {parentCategoryLookup.Count}");

// Subcategories: ProductSubcategoryID(0), ProductCategoryID(1), Name(2), rowguid(3), ModifiedDate(4)
var subcategoryLookup = productSubcategoryRows
    .ToDictionary(r => r[0].Trim(), r => new {
        SubcategoryId = r[0].Trim(),
        ParentCategoryId = r[1].Trim(),
        Name = r[2].Trim(),
        ModifiedDate = r[4].Trim()
    });
Console.WriteLine($"  Subcategories: {subcategoryLookup.Count}");

// Product descriptions: ProductDescriptionID → Description
// Columns: ProductDescriptionID(0), Description(1), rowguid(2), ModifiedDate(3)
var descriptionLookup = productDescriptionRows
    .ToDictionary(r => r[0].Trim(), r => r[1].Trim());

// PMPDC: ProductModelID → List of (CultureID, ProductDescriptionID)
// Columns: ProductModelID(0), ProductDescriptionID(1), CultureID(2), ModifiedDate(3)
var pmpdcByModel = pmpdcRows
    .GroupBy(r => r[0].Trim())
    .ToDictionary(g => g.Key, g => g.Select(r => new {
        Culture = r[2].Trim(),
        DescriptionId = r[1].Trim()
    }).ToList());

// Product models: ProductModelID → row
// Pipe columns: ProductModelID(0), Name(1), CatalogDescription(2), Instructions(3), rowguid(4), ModifiedDate(5)
var productModelLookup = productModelRows
    .ToDictionary(r => r[0].Trim(), r => new {
        Name = r[1].Trim(),
        CatalogDescription = NullIfEmpty(r[2]),
        ModifiedDate = r[5].Trim()
    });
Console.WriteLine($"  Product models: {productModelLookup.Count}");

// Products: ProductID → Name (for denormalization in order line items)
// Columns: ProductID(0), Name(1), ...
var productNameLookup = productRows
    .ToDictionary(r => r[0].Trim(), r => r[1].Trim());

// Addresses: AddressID → row
// Columns: AddressID(0), AddressLine1(1), AddressLine2(2), City(3), StateProvinceID(4),
//          PostalCode(5), SpatialLocation(6), rowguid(7), ModifiedDate(8)
var addressLookup = addressRows
    .ToDictionary(r => r[0].Trim(), r => r);

// StateProvince: StateProvinceID → (Name, CountryRegionCode)
// Columns: StateProvinceID(0), StateProvinceCode(1), CountryRegionCode(2),
//          IsOnlyStateProvinceFlag(3), Name(4), TerritoryID(5), rowguid(6), ModifiedDate(7)
var stateProvinceLookup = stateProvinceRows
    .ToDictionary(r => r[0].Trim(), r => (Name: r[4].Trim(), CountryRegionCode: r[2].Trim()));
Console.WriteLine($"  State/provinces: {stateProvinceLookup.Count}");

// CountryRegion: code → name (loaded from CSV)
var countryRegionLookup = new Dictionary<string, string>();
var countryRegionFile = Path.Combine(schemaDir, "CountryRegion.csv");
if (File.Exists(countryRegionFile))
{
    var crRows = ReadTabFile(countryRegionFile);
    foreach (var r in crRows)
    {
        var code = r[0].Trim();
        var name = r[1].Trim();
        countryRegionLookup.TryAdd(code, name);
    }
}
Console.WriteLine($"  Country/regions: {countryRegionLookup.Count}");

// ShipMethod: ShipMethodID → Name
// Columns: ShipMethodID(0), Name(1), ShipBase(2), ShipRate(3), rowguid(4), ModifiedDate(5)
var shipMethodLookup = shipMethodRows
    .ToDictionary(r => r[0].Trim(), r => r[1].Trim());
Console.WriteLine($"  Ship methods: {shipMethodLookup.Count}");

// Person: BusinessEntityID → row
// Pipe columns: BusinessEntityID(0), PersonType(1), NameStyle(2), Title(3), FirstName(4),
//               MiddleName(5), LastName(6), Suffix(7), EmailPromotion(8),
//               AdditionalContactInfo(9), Demographics(10), rowguid(11), ModifiedDate(12)
var personLookup = personRows
    .ToDictionary(r => r[0].Trim(), r => r);

// EmailAddress: BusinessEntityID → EmailAddress
// Pipe columns: BusinessEntityID(0), EmailAddressID(1), EmailAddress(2), rowguid(3), ModifiedDate(4)
var emailLookup = emailAddressRows
    .GroupBy(r => r[0].Trim())
    .ToDictionary(g => g.Key, g => g.First()[2].Trim());

// Password: BusinessEntityID → (PasswordHash, PasswordSalt)
// Pipe columns: BusinessEntityID(0), PasswordHash(1), PasswordSalt(2), rowguid(3), ModifiedDate(4)
var passwordLookup = passwordRows
    .ToDictionary(r => r[0].Trim(), r => new {
        Hash = r[1].Trim(),
        Salt = r[2].Trim()
    });

// PersonPhone: BusinessEntityID → PhoneNumber
// Pipe columns: BusinessEntityID(0), PhoneNumber(1), PhoneNumberTypeID(2), ModifiedDate(3)
var phoneLookup = personPhoneRows
    .GroupBy(r => r[0].Trim())
    .ToDictionary(g => g.Key, g => g.First()[1].Trim());

// BusinessEntityAddress: BusinessEntityID → List of (AddressID, AddressTypeID)
// Pipe columns: BusinessEntityID(0), AddressID(1), AddressTypeID(2), rowguid(3), ModifiedDate(4)
var beaByPerson = businessEntityAddressRows
    .GroupBy(r => r[0].Trim())
    .ToDictionary(g => g.Key, g => g.Select(r => new {
        AddressId = r[1].Trim(),
        AddressTypeId = r[2].Trim()
    }).ToList());

// AddressType: AddressTypeID → Name
// Pipe columns: AddressTypeID(0), Name(1), rowguid(2), ModifiedDate(3)
var addressTypeLookup = addressTypeRows
    .ToDictionary(r => r[0].Trim(), r => r[1].Trim());
Console.WriteLine($"  Address types: {addressTypeLookup.Count}");

// Customer: CustomerID → (PersonID, StoreID)
// Tab columns: CustomerID(0), PersonID(1), StoreID(2), TerritoryID(3), AccountNumber(4), rowguid(5), ModifiedDate(6)
var customerLookup = customerRows
    .Where(r => !string.IsNullOrWhiteSpace(r[1])) // Filter: must have PersonID (not store-only)
    .ToDictionary(r => r[0].Trim(), r => new {
        PersonId = r[1].Trim(),
        AccountNumber = r[4].Trim(),
        ModifiedDate = r[6].Trim()
    });
Console.WriteLine($"  Customers with PersonID: {customerLookup.Count}");

// SalesOrderDetail grouped by SalesOrderID
// Tab columns: SalesOrderID(0), SalesOrderDetailID(1), CarrierTrackingNumber(2), OrderQty(3),
//              ProductID(4), SpecialOfferID(5), UnitPrice(6), UnitPriceDiscount(7),
//              LineTotal(8), rowguid(9), ModifiedDate(10)
var orderDetailsByOrderId = salesOrderDetailRows
    .GroupBy(r => r[0].Trim())
    .ToDictionary(g => g.Key, g => g.ToList());
Console.WriteLine($"  Sales order detail groups: {orderDetailsByOrderId.Count}");

// ═══════════════════════════════════════════════════════════════════════
// STEP 3 — Transform and load: PRODUCTS CONTAINER
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("\n=== STEP 3: Loading PRODUCTS container ===");

int totalProductsDocs = 0;
int errorCount = 0;

// ─── 3a. Product Categories (parent + subcategories) ─────────────────
Console.WriteLine("\n--- 3a. Product Categories ---");
var categoryDocs = new List<Dictionary<string, object>>();

// Parent categories (IDs 1-4)
foreach (var row in productCategoryRows)
{
    var catId = row[0].Trim();
    categoryDocs.Add(new Dictionary<string, object>
    {
        ["id"] = $"category-{catId}",
        ["docType"] = "productCategory",
        ["productCategoryId"] = int.Parse(catId),
        ["parentProductCategoryId"] = (object)null,
        ["parentCategoryName"] = (object)null,
        ["name"] = row[1].Trim(),
        ["modifiedDate"] = ParseDate(row[3])
    });
}

// Subcategories (IDs offset by 100 to avoid collision with parent IDs)
foreach (var row in productSubcategoryRows)
{
    var subId = int.Parse(row[0].Trim());
    var parentCatId = row[1].Trim();
    var offsetId = subId + 100; // Offset to avoid collisions with parent category IDs
    var parentName = parentCategoryLookup.GetValueOrDefault(parentCatId, null);

    categoryDocs.Add(new Dictionary<string, object>
    {
        ["id"] = $"category-{offsetId}",
        ["docType"] = "productCategory",
        ["productCategoryId"] = offsetId,
        ["parentProductCategoryId"] = int.Parse(parentCatId),
        ["parentCategoryName"] = parentName,
        ["name"] = row[2].Trim(),
        ["modifiedDate"] = ParseDate(row[4])
    });
}

Console.WriteLine($"  Upserting {categoryDocs.Count} category documents...");
var catResults = await BulkUpsert(productsContainer, categoryDocs, d => new PartitionKey(d["id"].ToString()));
totalProductsDocs += catResults.success;
errorCount += catResults.errors;
Console.WriteLine($"  ✓ Categories: {catResults.success} succeeded, {catResults.errors} errors");

// ─── 3b. Product Models ──────────────────────────────────────────────
Console.WriteLine("\n--- 3b. Product Models ---");
var modelDocs = new List<Dictionary<string, object>>();

foreach (var row in productModelRows)
{
    var modelId = row[0].Trim();
    var descriptions = new List<Dictionary<string, object>>();

    if (pmpdcByModel.TryGetValue(modelId, out var descs))
    {
        foreach (var d in descs)
        {
            if (descriptionLookup.TryGetValue(d.DescriptionId, out var descText))
            {
                descriptions.Add(new Dictionary<string, object>
                {
                    ["culture"] = d.Culture,
                    ["description"] = descText
                });
            }
        }
    }

    modelDocs.Add(new Dictionary<string, object>
    {
        ["id"] = $"model-{modelId}",
        ["docType"] = "productModel",
        ["productModelId"] = int.Parse(modelId),
        ["name"] = row[1].Trim(),
        ["catalogDescription"] = NullIfEmpty(row[2]),
        ["descriptions"] = descriptions,
        ["modifiedDate"] = ParseDate(row[5])
    });
}

Console.WriteLine($"  Upserting {modelDocs.Count} product model documents...");
foreach (var batch in Batch(modelDocs, 100))
{
    var r = await BulkUpsert(productsContainer, batch, d => new PartitionKey(d["id"].ToString()));
    totalProductsDocs += r.success;
    errorCount += r.errors;
}
Console.WriteLine($"  ✓ Models: {modelDocs.Count} total");

// ─── 3c. Products ────────────────────────────────────────────────────
Console.WriteLine("\n--- 3c. Products ---");
var productDocs = new List<Dictionary<string, object>>();

// Build subcategory ID → (name, parentCategoryId, parentCategoryName)
var subcatInfo = productSubcategoryRows
    .ToDictionary(r => r[0].Trim(), r => {
        var parentCatId = r[1].Trim();
        return new {
            Name = r[2].Trim(),
            ParentCategoryId = parentCatId,
            ParentCategoryName = parentCategoryLookup.GetValueOrDefault(parentCatId, null),
            OffsetId = int.Parse(r[0].Trim()) + 100
        };
    });

foreach (var row in productRows)
{
    // Columns: ProductID(0), Name(1), ProductNumber(2), MakeFlag(3), FinishedGoodsFlag(4),
    //          Color(5), SafetyStockLevel(6), ReorderPoint(7), StandardCost(8), ListPrice(9),
    //          Size(10), SizeUnitMeasureCode(11), WeightUnitMeasureCode(12), Weight(13),
    //          DaysToManufacture(14), ProductLine(15), Class(16), Style(17),
    //          ProductSubcategoryID(18), ProductModelID(19),
    //          SellStartDate(20), SellEndDate(21), DiscontinuedDate(22),
    //          rowguid(23), ModifiedDate(24)

    var productId = row[0].Trim();
    var subcatId = row[18].Trim();
    var modelId = row[19].Trim();

    string categoryName = null, parentCategoryName = null;
    string categoryDocId = null;
    if (!string.IsNullOrEmpty(subcatId) && subcatInfo.TryGetValue(subcatId, out var sc))
    {
        categoryName = sc.Name;
        parentCategoryName = sc.ParentCategoryName;
        categoryDocId = $"category-{sc.OffsetId}";
    }

    string modelName = null;
    string modelDocId = null;
    if (!string.IsNullOrEmpty(modelId) && productModelLookup.TryGetValue(modelId, out var ml))
    {
        modelName = ml.Name;
        modelDocId = $"model-{modelId}";
    }

    productDocs.Add(new Dictionary<string, object>
    {
        ["id"] = $"product-{productId}",
        ["docType"] = "product",
        ["productId"] = int.Parse(productId),
        ["name"] = row[1].Trim(),
        ["productNumber"] = row[2].Trim(),
        ["color"] = NullIfEmpty(row[5]),
        ["standardCost"] = ParseDecimal(row[8]),
        ["listPrice"] = ParseDecimal(row[9]),
        ["size"] = NullIfEmpty(row[10]),
        ["weight"] = NullableDecimal(row[13]),
        ["productCategoryId"] = categoryDocId,
        ["categoryName"] = categoryName,
        ["parentCategoryName"] = parentCategoryName,
        ["productModelId"] = modelDocId,
        ["modelName"] = modelName,
        ["sellStartDate"] = ParseDate(row[20]),
        ["sellEndDate"] = NullableDate(row[21]),
        ["discontinuedDate"] = NullableDate(row[22]),
        ["thumbnailPhotoFileName"] = NullIfEmpty("no_image_available_small.gif"),
        ["modifiedDate"] = ParseDate(row[24])
    });
}

Console.WriteLine($"  Upserting {productDocs.Count} product documents (batches of 100)...");
int productBatch = 0;
foreach (var batch in Batch(productDocs, 100))
{
    productBatch++;
    var r = await BulkUpsert(productsContainer, batch, d => new PartitionKey(d["id"].ToString()));
    totalProductsDocs += r.success;
    errorCount += r.errors;
    Console.Write($"\r  Batch {productBatch}: {totalProductsDocs - catResults.success - modelDocs.Count} products loaded...");
}
Console.WriteLine($"\n  ✓ Products: {productDocs.Count} total");

Console.WriteLine($"\n  === Products container total: {totalProductsDocs} documents ===");

// ═══════════════════════════════════════════════════════════════════════
// STEP 4 — Transform and load: CUSTOMERS CONTAINER
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("\n=== STEP 4: Loading CUSTOMERS container ===");

int totalCustomersDocs = 0;

// ─── 4a. Customers ───────────────────────────────────────────────────
Console.WriteLine("\n--- 4a. Customers ---");
var customerDocs = new List<Dictionary<string, object>>();

foreach (var kvp in customerLookup)
{
    var custId = kvp.Key;
    var cust = kvp.Value;
    var personId = cust.PersonId;

    if (!personLookup.TryGetValue(personId, out var person))
        continue;

    // Build addresses
    var addresses = new List<Dictionary<string, object>>();
    if (beaByPerson.TryGetValue(personId, out var beaList))
    {
        foreach (var bea in beaList)
        {
            if (!addressLookup.TryGetValue(bea.AddressId, out var addr))
                continue;

            var stateProvinceId = addr[4].Trim();
            string stateName = null, countryName = null;
            if (stateProvinceLookup.TryGetValue(stateProvinceId, out var spTuple))
            {
                stateName = spTuple.Name;
                countryName = countryRegionLookup.GetValueOrDefault(spTuple.CountryRegionCode, spTuple.CountryRegionCode);
            }

            var addrTypeName = addressTypeLookup.GetValueOrDefault(bea.AddressTypeId, "Unknown");

            addresses.Add(new Dictionary<string, object>
            {
                ["addressType"] = addrTypeName,
                ["addressLine1"] = addr[1].Trim(),
                ["addressLine2"] = NullIfEmpty(addr[2]),
                ["city"] = addr[3].Trim(),
                ["stateProvince"] = stateName,
                ["countryRegion"] = countryName,
                ["postalCode"] = addr[5].Trim()
            });
        }
    }

    // Get email, phone, password
    var email = emailLookup.GetValueOrDefault(personId, null);
    var phone = phoneLookup.GetValueOrDefault(personId, null);
    passwordLookup.TryGetValue(personId, out var pwd);

    // Person columns: BusinessEntityID(0), PersonType(1), NameStyle(2), Title(3), FirstName(4),
    //                 MiddleName(5), LastName(6), Suffix(7), ...
    var firstName = person[4].Trim();
    var lastName = person[6].Trim();

    customerDocs.Add(new Dictionary<string, object>
    {
        ["id"] = custId,
        ["docType"] = "customer",
        ["customerId"] = custId,
        ["nameStyle"] = person[2].Trim() == "1",
        ["title"] = NullIfEmpty(person[3]),
        ["firstName"] = firstName,
        ["middleName"] = NullIfEmpty(person[5]),
        ["lastName"] = lastName,
        ["suffix"] = NullIfEmpty(person[7]),
        ["companyName"] = (object)null, // Not available in full AW schema CSV; SalesLT-only field
        ["salesPerson"] = (object)null, // Not available in full AW schema CSV
        ["emailAddress"] = email,
        ["phone"] = phone,
        ["passwordHash"] = pwd?.Hash,
        ["passwordSalt"] = pwd?.Salt,
        ["addresses"] = addresses,
        ["modifiedDate"] = ParseDate(cust.ModifiedDate)
    });
}

Console.WriteLine($"  Upserting {customerDocs.Count} customer documents (batches of 100)...");
int custBatch = 0;
foreach (var batch in Batch(customerDocs, 100))
{
    custBatch++;
    var r = await BulkUpsert(customersContainer, batch, d => new PartitionKey(d["customerId"].ToString()));
    totalCustomersDocs += r.success;
    errorCount += r.errors;
    if (custBatch % 10 == 0)
        Console.Write($"\r  Batch {custBatch}: {totalCustomersDocs} customers loaded...");
}
Console.WriteLine($"\n  ✓ Customers: {totalCustomersDocs} loaded");

// ─── 4b. Sales Orders ────────────────────────────────────────────────
Console.WriteLine("\n--- 4b. Sales Orders ---");
var orderDocs = new List<Dictionary<string, object>>();

// Build customerId → personId → name for denormalization
var custIdToName = new Dictionary<string, string>();
foreach (var kvp in customerLookup)
{
    var personId = kvp.Value.PersonId;
    if (personLookup.TryGetValue(personId, out var person))
    {
        var fn = person[4].Trim();
        var ln = person[6].Trim();
        custIdToName[kvp.Key] = $"{fn} {ln}";
    }
}

foreach (var row in salesOrderHeaderRows)
{
    // Columns: SalesOrderID(0), RevisionNumber(1), OrderDate(2), DueDate(3), ShipDate(4),
    //          Status(5), OnlineOrderFlag(6), SalesOrderNumber(7), PurchaseOrderNumber(8),
    //          AccountNumber(9), CustomerID(10), SalesPersonID(11), TerritoryID(12),
    //          BillToAddressID(13), ShipToAddressID(14), ShipMethodID(15),
    //          CreditCardID(16), CreditCardApprovalCode(17), CurrencyRateID(18),
    //          SubTotal(19), TaxAmt(20), Freight(21), TotalDue(22),
    //          Comment(23), rowguid(24), ModifiedDate(25)

    var orderId = row[0].Trim();
    var customerId = row[10].Trim();

    // Skip orders for customers we didn't load (store-only customers)
    if (!customerLookup.ContainsKey(customerId))
        continue;

    // Line items
    var lineItems = new List<Dictionary<string, object>>();
    if (orderDetailsByOrderId.TryGetValue(orderId, out var details))
    {
        foreach (var d in details)
        {
            var prodId = d[4].Trim();
            lineItems.Add(new Dictionary<string, object>
            {
                ["salesOrderDetailId"] = int.Parse(d[1].Trim()),
                ["productId"] = int.Parse(prodId),
                ["productName"] = productNameLookup.GetValueOrDefault(prodId, "Unknown"),
                ["orderQty"] = short.Parse(d[3].Trim()),
                ["unitPrice"] = ParseDecimal(d[6]),
                ["unitPriceDiscount"] = ParseDecimal(d[7]),
                ["lineTotal"] = ParseDecimal(d[8])
            });
        }
    }

    var billToAddr = BuildAddressSnapshot(row[13].Trim(), addressLookup, stateProvinceLookup, countryRegionLookup);
    var shipToAddr = BuildAddressSnapshot(row[14].Trim(), addressLookup, stateProvinceLookup, countryRegionLookup);

    // Ship method name
    var shipMethodId = row[15].Trim();
    var shipMethodName = shipMethodLookup.GetValueOrDefault(shipMethodId, null);

    var salesOrderId = int.Parse(orderId);

    orderDocs.Add(new Dictionary<string, object>
    {
        ["id"] = orderId,
        ["docType"] = "salesOrder",
        ["salesOrderId"] = salesOrderId,
        ["customerId"] = customerId,
        ["customerName"] = custIdToName.GetValueOrDefault(customerId, null),
        ["revisionNumber"] = int.Parse(row[1].Trim()),
        ["orderDate"] = ParseDate(row[2]),
        ["dueDate"] = ParseDate(row[3]),
        ["shipDate"] = NullableDate(row[4]),
        ["status"] = int.Parse(row[5].Trim()),
        ["onlineOrderFlag"] = row[6].Trim() == "1",
        ["salesOrderNumber"] = $"SO{salesOrderId}",
        ["purchaseOrderNumber"] = NullIfEmpty(row[8]),
        ["accountNumber"] = NullIfEmpty(row[9]),
        ["shipMethod"] = shipMethodName,
        ["creditCardApprovalCode"] = NullIfEmpty(row[17]),
        ["subTotal"] = ParseDecimal(row[19]),
        ["taxAmt"] = ParseDecimal(row[20]),
        ["freight"] = ParseDecimal(row[21]),
        ["totalDue"] = ParseDecimal(row[22]),
        ["comment"] = NullIfEmpty(row[23]),
        ["lineItems"] = lineItems,
        ["billToAddress"] = billToAddr,
        ["shipToAddress"] = shipToAddr,
        ["modifiedDate"] = ParseDate(row[25])
    });
}

Console.WriteLine($"  Upserting {orderDocs.Count} sales order documents (batches of 50)...");
int orderBatch = 0;
int orderLoaded = 0;
foreach (var batch in Batch(orderDocs, 50))
{
    orderBatch++;
    var r = await BulkUpsert(customersContainer, batch, d => new PartitionKey(d["customerId"].ToString()));
    totalCustomersDocs += r.success;
    orderLoaded += r.success;
    errorCount += r.errors;
    if (orderBatch % 20 == 0)
        Console.Write($"\r  Batch {orderBatch}: {orderLoaded} orders loaded...");
}
Console.WriteLine($"\n  ✓ Orders: {orderLoaded} loaded");

Console.WriteLine($"\n  === Customers container total: {totalCustomersDocs} documents ===");

// ═══════════════════════════════════════════════════════════════════════
// STEP 5 — Summary
// ═══════════════════════════════════════════════════════════════════════
sw.Stop();
Console.WriteLine("\n" + new string('═', 60));
Console.WriteLine("MIGRATION COMPLETE");
Console.WriteLine(new string('═', 60));
Console.WriteLine($"  Products container:  {totalProductsDocs} documents");
Console.WriteLine($"    - Categories: {categoryDocs.Count}");
Console.WriteLine($"    - Models:     {modelDocs.Count}");
Console.WriteLine($"    - Products:   {productDocs.Count}");
Console.WriteLine($"  Customers container: {totalCustomersDocs} documents");
Console.WriteLine($"    - Customers:  {customerDocs.Count}");
Console.WriteLine($"    - Orders:     {orderDocs.Count}");
Console.WriteLine($"  Grand total:         {totalProductsDocs + totalCustomersDocs} documents");
Console.WriteLine($"  Errors:              {errorCount}");
Console.WriteLine($"  Elapsed:             {sw.Elapsed:hh\\:mm\\:ss\\.fff}");
Console.WriteLine(new string('═', 60));

// ═══════════════════════════════════════════════════════════════════════
// Helper Functions
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Reads a tab-delimited BCP export (no headers).</summary>
static List<string[]> ReadTabFile(string path)
{
    var results = new List<string[]>();
    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        results.Add(line.Split('\t'));
    }
    return results;
}

/// <summary>Reads a pipe-delimited (+|) BCP export with &amp;| row terminators (no headers).</summary>
static List<string[]> ReadPipeFile(string path)
{
    var results = new List<string[]>();
    var rawText = File.ReadAllText(path);

    // Split on row terminator &| (followed by optional newline)
    var rows = rawText.Split("&|", StringSplitOptions.None);

    foreach (var row in rows)
    {
        var trimmed = row.Trim('\r', '\n');
        if (string.IsNullOrWhiteSpace(trimmed)) continue;
        // Split on field delimiter +|
        var fields = trimmed.Split("+|");
        results.Add(fields);
    }
    return results;
}

static string NullIfEmpty(string value)
{
    var v = value?.Trim();
    return string.IsNullOrEmpty(v) ? null : v;
}

static decimal ParseDecimal(string value)
{
    var v = value?.Trim();
    if (string.IsNullOrEmpty(v)) return 0m;
    return decimal.Parse(v, CultureInfo.InvariantCulture);
}

static decimal? NullableDecimal(string value)
{
    var v = value?.Trim();
    if (string.IsNullOrEmpty(v)) return null;
    return decimal.Parse(v, CultureInfo.InvariantCulture);
}

static string ParseDate(string value)
{
    var v = value?.Trim();
    if (string.IsNullOrEmpty(v)) return null;
    // Parse and re-format as ISO 8601 UTC
    if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        return dt.ToString("yyyy-MM-ddTHH:mm:ssZ");
    return v;
}

static string NullableDate(string value)
{
    var v = value?.Trim();
    if (string.IsNullOrEmpty(v)) return null;
    return ParseDate(v);
}

static Dictionary<string, object> BuildAddressSnapshot(
    string addressId,
    Dictionary<string, string[]> addrLookup,
    Dictionary<string, (string Name, string CountryRegionCode)> spLookup,
    Dictionary<string, string> crLookup)
{
    if (string.IsNullOrEmpty(addressId) || !addrLookup.TryGetValue(addressId, out var addr))
        return null;

    string stateName = null, countryName = null;
    var spId = addr[4].Trim();
    if (spLookup.TryGetValue(spId, out var sp))
    {
        stateName = sp.Name;
        countryName = crLookup.GetValueOrDefault(sp.CountryRegionCode, sp.CountryRegionCode);
    }

    return new Dictionary<string, object>
    {
        ["addressLine1"] = addr[1].Trim(),
        ["addressLine2"] = NullIfEmpty(addr[2]),
        ["city"] = addr[3].Trim(),
        ["stateProvince"] = stateName,
        ["countryRegion"] = countryName,
        ["postalCode"] = addr[5].Trim()
    };
}

static IEnumerable<List<T>> Batch<T>(List<T> source, int batchSize)
{
    for (int i = 0; i < source.Count; i += batchSize)
    {
        yield return source.GetRange(i, Math.Min(batchSize, source.Count - i));
    }
}

/// <summary>Bulk-upsert documents. Returns (success, errors) counts.</summary>
static async Task<(int success, int errors)> BulkUpsert(
    Container container,
    List<Dictionary<string, object>> docs,
    Func<Dictionary<string, object>, PartitionKey> pkSelector)
{
    var tasks = new List<Task<(bool ok, string id)>>();

    foreach (var doc in docs)
    {
        var pk = pkSelector(doc);
        tasks.Add(UpsertOne(container, doc, pk));
    }

    await Task.WhenAll(tasks);

    int success = 0, errors = 0;
    foreach (var t in tasks)
    {
        if (t.Result.ok) success++;
        else errors++;
    }
    return (success, errors);
}

static async Task<(bool ok, string id)> UpsertOne(
    Container container, Dictionary<string, object> doc, PartitionKey pk)
{
    var id = doc.ContainsKey("id") ? doc["id"]?.ToString() : "?";
    try
    {
        await container.UpsertItemAsync(doc, pk);
        return (true, id);
    }
    catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
    {
        // Retry after throttle
        await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(1));
        try
        {
            await container.UpsertItemAsync(doc, pk);
            return (true, id);
        }
        catch (Exception ex2)
        {
            Console.Error.WriteLine($"\n  ERROR (retry) id={id}: {ex2.Message}");
            return (false, id);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"\n  ERROR id={id}: {ex.Message}");
        return (false, id);
    }
}

static string FindSchemaDir()
{
    // Walk up from the executable to find the schema/ folder
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 10; i++)
    {
        var candidate = Path.Combine(dir, "schema");
        if (Directory.Exists(candidate)) return candidate;
        dir = Path.GetDirectoryName(dir);
        if (dir == null) break;
    }
    // Try relative to current directory
    var cwd = Directory.GetCurrentDirectory();
    for (int i = 0; i < 10; i++)
    {
        var candidate = Path.Combine(cwd, "schema");
        if (Directory.Exists(candidate)) return candidate;
        cwd = Path.GetDirectoryName(cwd);
        if (cwd == null) break;
    }
    throw new DirectoryNotFoundException("Cannot find schema/ folder. Run from the AdventureWorks.Web directory.");
}
