/**
 * AdventureWorks CSV → Cosmos DB JSON Converter & Loader
 * ======================================================
 * Reads CSV source data from AdventureWorksLT, converts to Cosmos DB NoSQL
 * document schemas per schema_and_access_patterns_conversion_plan.md,
 * saves JSON files to DataMigration/data/, and loads into Cosmos DB.
 *
 * Usage:
 *   node convert_and_load.js [--generate] [--load] [--validate] [--all]
 */

const fs = require("fs");
const path = require("path");
const { parse } = require("csv-parse/sync");

// ---------------------------------------------------------------------------
// Paths & Config
// ---------------------------------------------------------------------------
const PROJECT_ROOT = path.resolve(__dirname, "..", "..");
const CSV_DIR = path.join(PROJECT_ROOT, "AdventureWorksLT", "AdventureWorksLT");
const DATA_DIR = path.join(__dirname, "..", "data");

const COSMOS_ENDPOINT = "https://ssm-cosmos-adwlt01.documents.azure.com:443/";
const DATABASE_NAME = "adwkslt";
const CUSTOMER_ORDERS_CONTAINER = "customer-orders";
const PRODUCT_CATALOG_CONTAINER = "product-catalog";

// ---------------------------------------------------------------------------
// CSV Helpers
// ---------------------------------------------------------------------------
function readCsv(filename) {
  const content = fs.readFileSync(path.join(CSV_DIR, filename), "utf-8");
  return parse(content, { columns: true, skip_empty_lines: true, bom: true });
}

/** Parse European decimals: "880,3484" → 880.3484 */
function parseDecimal(val) {
  if (!val || val.trim() === "") return null;
  return parseFloat(val.replace(",", "."));
}

function parseInt2(val) {
  if (!val || val.trim() === "") return null;
  return parseInt(val, 10);
}

function parseBool(val) {
  return val && val.trim().toLowerCase() === "true";
}

/** "2008-06-01 00:00:00.000" → "2008-06-01T00:00:00Z" */
function parseDate(val) {
  if (!val || val.trim() === "") return null;
  const parts = val.trim().split(".")[0]; // drop ms
  return parts.replace(" ", "T") + "Z";
}

function noneIfEmpty(val) {
  if (!val || val.trim() === "") return null;
  return val.trim();
}

function round4(n) {
  return Math.round(n * 10000) / 10000;
}

// ---------------------------------------------------------------------------
// Lookup Builders
// ---------------------------------------------------------------------------
function buildAddressLookup(addresses) {
  const map = {};
  for (const a of addresses) {
    const id = parseInt(a.AddressID, 10);
    map[id] = {
      addressId: id,
      addressLine1: noneIfEmpty(a.AddressLine1),
      addressLine2: noneIfEmpty(a.AddressLine2),
      city: noneIfEmpty(a.City),
      stateProvince: noneIfEmpty(a.StateProvince),
      countryRegion: noneIfEmpty(a.CountryRegion),
      postalCode: noneIfEmpty(a.PostalCode),
    };
  }
  return map;
}

function buildCustomerAddressMap(custAddrs) {
  const map = {};
  for (const ca of custAddrs) {
    const cid = parseInt(ca.CustomerID, 10);
    if (!map[cid]) map[cid] = [];
    map[cid].push({
      addressId: parseInt(ca.AddressID, 10),
      addressType: noneIfEmpty(ca.AddressType),
    });
  }
  return map;
}

function buildCategoryLookup(categories) {
  const map = {};
  for (const c of categories) {
    const cid = parseInt(c.ProductCategoryID, 10);
    map[cid] = {
      name: noneIfEmpty(c.Name),
      parentId: parseInt2(c.ParentProductCategoryID),
    };
  }
  // Resolve parent names
  for (const cid of Object.keys(map)) {
    const info = map[cid];
    info.parentName =
      info.parentId && map[info.parentId] ? map[info.parentId].name : null;
  }
  return map;
}

function buildModelLookup(models) {
  const map = {};
  for (const m of models) {
    const mid = parseInt(m.ProductModelID, 10);
    map[mid] = {
      name: noneIfEmpty(m.Name),
      catalogDescription: noneIfEmpty(m.CatalogDescription),
    };
  }
  return map;
}

function buildDescriptionLookup(descriptions) {
  const map = {};
  for (const d of descriptions) {
    map[parseInt(d.ProductDescriptionID, 10)] = noneIfEmpty(d.Description);
  }
  return map;
}

function buildModelDescriptionsMap(junctions, descLookup) {
  const map = {};
  for (const j of junctions) {
    const mid = parseInt(j.ProductModelID, 10);
    const did = parseInt(j.ProductDescriptionID, 10);
    const culture = j.Culture.trim();
    if (!map[mid]) map[mid] = [];
    map[mid].push({ culture, description: descLookup[did] || "" });
  }
  return map;
}

function buildProductLookup(products) {
  const map = {};
  for (const p of products) {
    map[parseInt(p.ProductID, 10)] = {
      name: noneIfEmpty(p.Name),
      productNumber: noneIfEmpty(p.ProductNumber),
    };
  }
  return map;
}

// ---------------------------------------------------------------------------
// Converters
// ---------------------------------------------------------------------------
function convertCustomers(customers, custAddrMap, addrLookup) {
  return customers.map((c) => {
    const cid = parseInt(c.CustomerID, 10);
    const addresses = (custAddrMap[cid] || [])
      .map((ca) => {
        const addr = addrLookup[ca.addressId];
        if (!addr) return null;
        return { ...addr, addressType: ca.addressType };
      })
      .filter(Boolean);

    return {
      id: `customer-${cid}`,
      type: "customer",
      customerId: cid,
      nameStyle: parseBool(c.NameStyle),
      title: noneIfEmpty(c.Title),
      firstName: noneIfEmpty(c.FirstName),
      middleName: noneIfEmpty(c.MiddleName),
      lastName: noneIfEmpty(c.LastName),
      suffix: noneIfEmpty(c.Suffix),
      companyName: noneIfEmpty(c.CompanyName),
      salesPerson: noneIfEmpty(c.SalesPerson),
      emailAddress: noneIfEmpty(c.EmailAddress),
      phone: noneIfEmpty(c.Phone),
      addresses,
      modifiedDate: parseDate(c.ModifiedDate),
    };
  });
}

function convertSalesOrders(headers, details, addrLookup, productLookup) {
  // Group details by order
  const detailsByOrder = {};
  for (const d of details) {
    const soid = parseInt(d.SalesOrderID, 10);
    if (!detailsByOrder[soid]) detailsByOrder[soid] = [];
    detailsByOrder[soid].push(d);
  }

  const toSnapshot = (a) => ({
    addressLine1: a ? a.addressLine1 : null,
    addressLine2: a ? a.addressLine2 : null,
    city: a ? a.city : null,
    stateProvince: a ? a.stateProvince : null,
    countryRegion: a ? a.countryRegion : null,
    postalCode: a ? a.postalCode : null,
  });

  return headers.map((h) => {
    const soid = parseInt(h.SalesOrderID, 10);
    const cid = parseInt(h.CustomerID, 10);

    const shipAddr = addrLookup[parseInt2(h.ShipToAddressID)] || null;
    const billAddr = addrLookup[parseInt2(h.BillToAddressID)] || null;

    const orderDetails = (detailsByOrder[soid] || []).map((d) => {
      const pid = parseInt(d.ProductID, 10);
      const prod = productLookup[pid] || {};
      const qty = parseInt(d.OrderQty, 10);
      const unitPrice = parseDecimal(d.UnitPrice) || 0;
      const discount = parseDecimal(d.UnitPriceDiscount) || 0;
      return {
        salesOrderDetailId: parseInt(d.SalesOrderDetailID, 10),
        productId: pid,
        productName: prod.name || null,
        productNumber: prod.productNumber || null,
        orderQty: qty,
        unitPrice: round4(unitPrice),
        unitPriceDiscount: round4(discount),
        lineTotal: round4(unitPrice * (1.0 - discount) * qty),
      };
    });

    const subTotal = parseDecimal(h.SubTotal) || 0;
    const taxAmt = parseDecimal(h.TaxAmt) || 0;
    const freight = parseDecimal(h.Freight) || 0;

    return {
      id: `order-${soid}`,
      type: "salesOrder",
      salesOrderId: soid,
      customerId: cid,
      revisionNumber: parseInt(h.RevisionNumber, 10),
      orderDate: parseDate(h.OrderDate),
      dueDate: parseDate(h.DueDate),
      shipDate: parseDate(h.ShipDate),
      status: parseInt(h.Status, 10),
      onlineOrderFlag: parseBool(h.OnlineOrderFlag),
      salesOrderNumber: `SO${soid}`,
      purchaseOrderNumber: noneIfEmpty(h.PurchaseOrderNumber),
      accountNumber: noneIfEmpty(h.AccountNumber),
      shipMethod: noneIfEmpty(h.ShipMethod),
      creditCardApprovalCode: noneIfEmpty(h.CreditCardApprovalCode),
      subTotal: round4(subTotal),
      taxAmt: round4(taxAmt),
      freight: round4(freight),
      totalDue: round4(subTotal + taxAmt + freight),
      comment: noneIfEmpty(h.Comment),
      shipToAddress: toSnapshot(shipAddr),
      billToAddress: toSnapshot(billAddr),
      orderDetails,
      modifiedDate: parseDate(h.ModifiedDate),
    };
  });
}

function convertProducts(products, catLookup, modelLookup, modelDescMap) {
  return products.map((p) => {
    const pid = parseInt(p.ProductID, 10);
    const catId = parseInt2(p.ProductCategoryID);
    const modelId = parseInt2(p.ProductModelID);
    const catInfo = catId ? catLookup[catId] || {} : {};
    const modelInfo = modelId ? modelLookup[modelId] || {} : {};
    const allDescs = modelId ? modelDescMap[modelId] || [] : [];
    const enDescs = allDescs.filter((d) => d.culture === "en");

    return {
      id: `product-${pid}`,
      type: "product",
      productId: pid,
      productCategoryId: catId,
      name: noneIfEmpty(p.Name),
      productNumber: noneIfEmpty(p.ProductNumber),
      color: noneIfEmpty(p.Color),
      standardCost: parseDecimal(p.StandardCost),
      listPrice: parseDecimal(p.ListPrice),
      size: noneIfEmpty(p.Size),
      weight: parseDecimal(p.Weight),
      categoryName: catInfo.name || null,
      parentCategoryName: catInfo.parentName || null,
      productModelId: modelId,
      productModelName: modelInfo.name || null,
      descriptions: enDescs,
      sellStartDate: parseDate(p.SellStartDate),
      sellEndDate: parseDate(p.SellEndDate),
      discontinuedDate: parseDate(p.DiscontinuedDate),
      thumbnailPhotoUrl: null,
      thumbnailPhotoFileName: noneIfEmpty(p.ThumbnailPhotoFileName),
      modifiedDate: parseDate(p.ModifiedDate),
    };
  });
}

function convertProductCategories(categories, catLookup) {
  return categories.map((c) => {
    const cid = parseInt(c.ProductCategoryID, 10);
    const info = catLookup[cid];
    return {
      id: `category-${cid}`,
      type: "productCategory",
      productCategoryId: cid,
      parentProductCategoryId: info.parentId,
      parentCategoryName: info.parentName,
      name: info.name,
      modifiedDate: parseDate(c.ModifiedDate),
    };
  });
}

function convertProductModels(models, modelDescMap) {
  return models.map((m) => {
    const mid = parseInt(m.ProductModelID, 10);
    return {
      id: `model-${mid}`,
      type: "productModel",
      productModelId: mid,
      productCategoryId: 0, // Synthetic PK
      name: noneIfEmpty(m.Name),
      catalogDescription: noneIfEmpty(m.CatalogDescription),
      descriptions: modelDescMap[mid] || [],
      modifiedDate: parseDate(m.ModifiedDate),
    };
  });
}

// ---------------------------------------------------------------------------
// Save JSON
// ---------------------------------------------------------------------------
function saveJson(docs, filename) {
  const filePath = path.join(DATA_DIR, filename);
  fs.writeFileSync(filePath, JSON.stringify(docs, null, 2), "utf-8");
  const rel = path.relative(PROJECT_ROOT, filePath);
  console.log(`  Saved ${String(docs.length).padStart(5)} docs → ${rel}`);
}

// ---------------------------------------------------------------------------
// Phase: Generate
// ---------------------------------------------------------------------------
function generate() {
  console.log("=".repeat(60));
  console.log("Phase 1: Reading CSV source files...");
  console.log("=".repeat(60));

  const customersRaw = readCsv("Customer.csv");
  const addressesRaw = readCsv("Address.csv");
  const custAddrRaw = readCsv("CustomerAddress.csv");
  const ordersRaw = readCsv("SalesOrderHeader.csv");
  const detailsRaw = readCsv("SalesOrderDetail.csv");
  const productsRaw = readCsv("Product.csv");
  const categoriesRaw = readCsv("ProductCategory.csv");
  const modelsRaw = readCsv("ProductModel.csv");
  const descriptionsRaw = readCsv("ProductDescription.csv");
  const modelDescRaw = readCsv("ProductModelProductDescription.csv");

  console.log(`  Customer:           ${String(customersRaw.length).padStart(5)} rows`);
  console.log(`  Address:            ${String(addressesRaw.length).padStart(5)} rows`);
  console.log(`  CustomerAddress:    ${String(custAddrRaw.length).padStart(5)} rows`);
  console.log(`  SalesOrderHeader:   ${String(ordersRaw.length).padStart(5)} rows`);
  console.log(`  SalesOrderDetail:   ${String(detailsRaw.length).padStart(5)} rows`);
  console.log(`  Product:            ${String(productsRaw.length).padStart(5)} rows`);
  console.log(`  ProductCategory:    ${String(categoriesRaw.length).padStart(5)} rows`);
  console.log(`  ProductModel:       ${String(modelsRaw.length).padStart(5)} rows`);
  console.log(`  ProductDescription: ${String(descriptionsRaw.length).padStart(5)} rows`);
  console.log(`  ModelProdDesc:      ${String(modelDescRaw.length).padStart(5)} rows`);

  console.log("\n" + "=".repeat(60));
  console.log("Phase 2: Building lookup tables...");
  console.log("=".repeat(60));

  const addrLookup = buildAddressLookup(addressesRaw);
  const custAddrMap = buildCustomerAddressMap(custAddrRaw);
  const catLookup = buildCategoryLookup(categoriesRaw);
  const modelLookup = buildModelLookup(modelsRaw);
  const descLookup = buildDescriptionLookup(descriptionsRaw);
  const modelDescMap = buildModelDescriptionsMap(modelDescRaw, descLookup);
  const productLookup = buildProductLookup(productsRaw);
  console.log("  Lookups built successfully.");

  console.log("\n" + "=".repeat(60));
  console.log("Phase 3: Converting to Cosmos DB documents...");
  console.log("=".repeat(60));

  const customerDocs = convertCustomers(customersRaw, custAddrMap, addrLookup);
  const orderDocs = convertSalesOrders(ordersRaw, detailsRaw, addrLookup, productLookup);
  const productDocs = convertProducts(productsRaw, catLookup, modelLookup, modelDescMap);
  const categoryDocs = convertProductCategories(categoriesRaw, catLookup);
  const modelDocs = convertProductModels(modelsRaw, modelDescMap);

  console.log(`  customer docs:        ${String(customerDocs.length).padStart(5)}`);
  console.log(`  salesOrder docs:      ${String(orderDocs.length).padStart(5)}`);
  console.log(`  product docs:         ${String(productDocs.length).padStart(5)}`);
  console.log(`  productCategory docs: ${String(categoryDocs.length).padStart(5)}`);
  console.log(`  productModel docs:    ${String(modelDocs.length).padStart(5)}`);
  const total = customerDocs.length + orderDocs.length + productDocs.length + categoryDocs.length + modelDocs.length;
  console.log(`  TOTAL:                ${String(total).padStart(5)}`);

  console.log("\n" + "=".repeat(60));
  console.log("Phase 4: Saving JSON files to DataMigration/data/...");
  console.log("=".repeat(60));

  fs.mkdirSync(DATA_DIR, { recursive: true });

  saveJson(customerDocs, "customers.json");
  saveJson(orderDocs, "sales-orders.json");
  saveJson(productDocs, "products.json");
  saveJson(categoryDocs, "product-categories.json");
  saveJson(modelDocs, "product-models.json");

  const coAll = [...customerDocs, ...orderDocs];
  saveJson(coAll, "customer-orders-container.json");

  const pcAll = [...productDocs, ...categoryDocs, ...modelDocs];
  saveJson(pcAll, "product-catalog-container.json");

  console.log(`\n  Total documents for customer-orders: ${coAll.length}`);
  console.log(`  Total documents for product-catalog: ${pcAll.length}`);

  return { coAll, pcAll };
}

// ---------------------------------------------------------------------------
// Phase: Load to Cosmos DB
// ---------------------------------------------------------------------------
async function loadToCosmos(coAll, pcAll) {
  const { CosmosClient } = require("@azure/cosmos");
  const { DefaultAzureCredential } = require("@azure/identity");

  console.log("\n" + "=".repeat(60));
  console.log("Phase 5: Loading documents into Cosmos DB...");
  console.log("=".repeat(60));

  if (!coAll) {
    coAll = JSON.parse(fs.readFileSync(path.join(DATA_DIR, "customer-orders-container.json"), "utf-8"));
  }
  if (!pcAll) {
    pcAll = JSON.parse(fs.readFileSync(path.join(DATA_DIR, "product-catalog-container.json"), "utf-8"));
  }

  const credential = new DefaultAzureCredential();
  const client = new CosmosClient({ endpoint: COSMOS_ENDPOINT, aadCredentials: credential });
  const database = client.database(DATABASE_NAME);

  // Load customer-orders
  const coCont = database.container(CUSTOMER_ORDERS_CONTAINER);
  console.log(`\n  Loading ${coAll.length} docs into '${CUSTOMER_ORDERS_CONTAINER}'...`);
  let successCo = 0, failCo = 0;
  for (const doc of coAll) {
    try {
      await coCont.items.upsert(doc);
      successCo++;
    } catch (e) {
      failCo++;
      console.log(`    ERROR [${doc.id}]: ${e.message}`);
    }
  }
  console.log(`    Success: ${successCo}, Failed: ${failCo}`);

  // Load product-catalog
  const pcCont = database.container(PRODUCT_CATALOG_CONTAINER);
  console.log(`\n  Loading ${pcAll.length} docs into '${PRODUCT_CATALOG_CONTAINER}'...`);
  let successPc = 0, failPc = 0;
  for (const doc of pcAll) {
    try {
      await pcCont.items.upsert(doc);
      successPc++;
    } catch (e) {
      failPc++;
      console.log(`    ERROR [${doc.id}]: ${e.message}`);
    }
  }
  console.log(`    Success: ${successPc}, Failed: ${failPc}`);

  const totalSuccess = successCo + successPc;
  const totalFail = failCo + failPc;
  console.log(`\n  TOTAL: ${totalSuccess} loaded, ${totalFail} failed`);
  return totalFail === 0;
}

// ---------------------------------------------------------------------------
// Phase: Validate
// ---------------------------------------------------------------------------
async function validate() {
  const { CosmosClient } = require("@azure/cosmos");
  const { DefaultAzureCredential } = require("@azure/identity");

  console.log("\n" + "=".repeat(60));
  console.log("Phase 6: Validating data migration...");
  console.log("=".repeat(60));

  const credential = new DefaultAzureCredential();
  const client = new CosmosClient({ endpoint: COSMOS_ENDPOINT, aadCredentials: credential });
  const database = client.database(DATABASE_NAME);

  // Read source JSON files to get actual expected counts
  const coData = JSON.parse(fs.readFileSync(path.join(DATA_DIR, "customer-orders-container.json"), "utf-8"));
  const pcData = JSON.parse(fs.readFileSync(path.join(DATA_DIR, "product-catalog-container.json"), "utf-8"));

  const expectedCustomers = coData.filter(d => d.type === "customer").length;
  const expectedOrders = coData.filter(d => d.type === "salesOrder").length;
  const expectedProducts = pcData.filter(d => d.type === "product").length;
  const expectedCategories = pcData.filter(d => d.type === "productCategory").length;
  const expectedModels = pcData.filter(d => d.type === "productModel").length;

  const expected = {
    [CUSTOMER_ORDERS_CONTAINER]: { customer: expectedCustomers, salesOrder: expectedOrders },
    [PRODUCT_CATALOG_CONTAINER]: { product: expectedProducts, productCategory: expectedCategories, productModel: expectedModels },
  };

  let allPassed = true;

  for (const [containerName, typeCounts] of Object.entries(expected)) {
    const container = database.container(containerName);
    console.log(`\n  Container: ${containerName}`);

    // Total count
    const { resources: totalRes } = await container.items
      .query("SELECT VALUE COUNT(1) FROM c")
      .fetchAll();
    const totalCount = totalRes[0];
    const expectedTotal = Object.values(typeCounts).reduce((a, b) => a + b, 0);
    let status = totalCount === expectedTotal ? "PASS" : "FAIL";
    if (status === "FAIL") allPassed = false;
    console.log(`    Total documents: ${totalCount} (expected ${expectedTotal}) [${status}]`);

    // By type
    for (const [docType, expCount] of Object.entries(typeCounts)) {
      const { resources: typeRes } = await container.items
        .query(`SELECT VALUE COUNT(1) FROM c WHERE c.type = '${docType}'`)
        .fetchAll();
      const actual = typeRes[0];
      status = actual === expCount ? "PASS" : "FAIL";
      if (status === "FAIL") allPassed = false;
      console.log(`    type='${docType}': ${actual} (expected ${expCount}) [${status}]`);
    }
  }

  // Spot checks
  console.log("\n  Spot-check validations:");
  const coCont = database.container(CUSTOMER_ORDERS_CONTAINER);
  const pcCont = database.container(PRODUCT_CATALOG_CONTAINER);

  // customer-1
  try {
    const { resource: cust1 } = await coCont.item("customer-1", 1).read();
    const checks = [
      ["customer-1 exists", !!cust1],
      ["customer-1 type=customer", cust1.type === "customer"],
      ["customer-1 firstName=Orlando", cust1.firstName === "Orlando"],
      ["customer-1 has addresses[]", Array.isArray(cust1.addresses)],
      ["customer-1 no passwordHash", !("passwordHash" in cust1)],
    ];
    for (const [desc, passed] of checks) {
      status = passed ? "PASS" : "FAIL";
      if (!passed) allPassed = false;
      console.log(`    ${desc}: [${status}]`);
    }
  } catch (e) {
    console.log(`    customer-1 read FAILED: ${e.message}`);
    allPassed = false;
  }

  // order-71774
  try {
    const { resource: order } = await coCont.item("order-71774", 29847).read();
    const checks = [
      ["order-71774 exists", !!order],
      ["order-71774 type=salesOrder", order.type === "salesOrder"],
      ["order-71774 has orderDetails[]", Array.isArray(order.orderDetails) && order.orderDetails.length > 0],
      ["order-71774 has shipToAddress", !!order.shipToAddress],
      ["order-71774 salesOrderNumber=SO71774", order.salesOrderNumber === "SO71774"],
      ["order-71774 detail has productName", !!order.orderDetails[0].productName],
    ];
    for (const [desc, passed] of checks) {
      status = passed ? "PASS" : "FAIL";
      if (!passed) allPassed = false;
      console.log(`    ${desc}: [${status}]`);
    }
  } catch (e) {
    console.log(`    order-71774 read FAILED: ${e.message}`);
    allPassed = false;
  }

  // product-680
  try {
    const { resource: prod } = await pcCont.item("product-680", 18).read();
    const checks = [
      ["product-680 exists", !!prod],
      ["product-680 type=product", prod.type === "product"],
      ["product-680 categoryName=Road Frames", prod.categoryName === "Road Frames"],
      ["product-680 productModelName=HL Road Frame", prod.productModelName === "HL Road Frame"],
      ["product-680 has descriptions[]", Array.isArray(prod.descriptions)],
    ];
    for (const [desc, passed] of checks) {
      status = passed ? "PASS" : "FAIL";
      if (!passed) allPassed = false;
      console.log(`    ${desc}: [${status}]`);
    }
  } catch (e) {
    console.log(`    product-680 read FAILED: ${e.message}`);
    allPassed = false;
  }

  // category-18
  try {
    const { resource: cat } = await pcCont.item("category-18", 18).read();
    const checks = [
      ["category-18 exists", !!cat],
      ["category-18 type=productCategory", cat.type === "productCategory"],
      ["category-18 name=Road Frames", cat.name === "Road Frames"],
      ["category-18 parentCategoryName=Components", cat.parentCategoryName === "Components"],
    ];
    for (const [desc, passed] of checks) {
      status = passed ? "PASS" : "FAIL";
      if (!passed) allPassed = false;
      console.log(`    ${desc}: [${status}]`);
    }
  } catch (e) {
    console.log(`    category-18 read FAILED: ${e.message}`);
    allPassed = false;
  }

  // model-6 (PK=0)
  try {
    const { resource: model } = await pcCont.item("model-6", 0).read();
    const checks = [
      ["model-6 exists", !!model],
      ["model-6 type=productModel", model.type === "productModel"],
      ["model-6 productCategoryId=0", model.productCategoryId === 0],
      ["model-6 name=HL Road Frame", model.name === "HL Road Frame"],
      ["model-6 has descriptions[]", Array.isArray(model.descriptions) && model.descriptions.length > 0],
    ];
    for (const [desc, passed] of checks) {
      status = passed ? "PASS" : "FAIL";
      if (!passed) allPassed = false;
      console.log(`    ${desc}: [${status}]`);
    }
  } catch (e) {
    console.log(`    model-6 read FAILED: ${e.message}`);
    allPassed = false;
  }

  console.log("\n" + "=".repeat(60));
  if (allPassed) {
    console.log("VALIDATION RESULT: ALL CHECKS PASSED");
  } else {
    console.log("VALIDATION RESULT: SOME CHECKS FAILED");
  }
  console.log("=".repeat(60));
  return allPassed;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
async function main() {
  const args = process.argv.slice(2);
  const doGenerate = args.includes("--generate") || args.includes("--all") || args.length === 0;
  const doLoad = args.includes("--load") || args.includes("--all") || args.length === 0;
  const doValidate = args.includes("--validate") || args.includes("--all") || args.length === 0;

  let coAll = null, pcAll = null;

  if (doGenerate) {
    const result = generate();
    coAll = result.coAll;
    pcAll = result.pcAll;
  }

  if (doLoad) {
    await loadToCosmos(coAll, pcAll);
  }

  if (doValidate) {
    await validate();
  }
}

main().catch((err) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
