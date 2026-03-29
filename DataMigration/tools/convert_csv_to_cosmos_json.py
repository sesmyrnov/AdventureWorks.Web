"""
AdventureWorks CSV → Cosmos DB JSON Converter & Loader
======================================================
Reads CSV source data from AdventureWorksLT, converts to Cosmos DB NoSQL
document schemas per the schema_and_access_patterns_conversion_plan.md,
saves JSON files to DataMigration/data/, and loads into Cosmos DB.

Containers:
  - customer-orders  (PK: /customerId)  → customer + salesOrder docs
  - product-catalog   (PK: /productCategoryId) → product + productCategory + productModel docs

Usage:
  pip install azure-cosmos azure-identity
  python convert_csv_to_cosmos_json.py [--generate] [--load] [--validate]
"""

import csv
import json
import os
import sys
import argparse
from datetime import datetime
from pathlib import Path

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
CSV_DIR = PROJECT_ROOT / "AdventureWorksLT" / "AdventureWorksLT"
DATA_DIR = SCRIPT_DIR.parent / "data"

COSMOS_ACCOUNT = "ssm-cosmos-adwlt01"
COSMOS_ENDPOINT = f"https://{COSMOS_ACCOUNT}.documents.azure.com:443/"
DATABASE_NAME = "adwkslt"
CUSTOMER_ORDERS_CONTAINER = "customer-orders"
PRODUCT_CATALOG_CONTAINER = "product-catalog"

# ---------------------------------------------------------------------------
# CSV helpers
# ---------------------------------------------------------------------------

def read_csv(filename: str) -> list[dict]:
    """Read a CSV file and return a list of dicts."""
    path = CSV_DIR / filename
    with open(path, "r", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        return list(reader)


def parse_decimal(value: str) -> float | None:
    """Parse European-format decimal (comma as decimal separator)."""
    if not value or value.strip() == "":
        return None
    # CSV uses comma as decimal separator: "880,3484" → 880.3484
    return float(value.replace(",", "."))


def parse_int(value: str) -> int | None:
    if not value or value.strip() == "":
        return None
    return int(value)


def parse_bool(value: str) -> bool:
    return value.strip().lower() == "true"


def parse_date(value: str) -> str | None:
    """Convert '2008-06-01 00:00:00.000' to ISO 8601 UTC."""
    if not value or value.strip() == "":
        return None
    dt = datetime.strptime(value.strip().split(".")[0], "%Y-%m-%d %H:%M:%S")
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


def none_if_empty(value: str) -> str | None:
    if not value or value.strip() == "":
        return None
    return value.strip()


# ---------------------------------------------------------------------------
# Build lookup dictionaries
# ---------------------------------------------------------------------------

def build_address_lookup(addresses: list[dict]) -> dict:
    """AddressID → address dict."""
    lookup = {}
    for a in addresses:
        aid = int(a["AddressID"])
        lookup[aid] = {
            "addressId": aid,
            "addressLine1": none_if_empty(a["AddressLine1"]),
            "addressLine2": none_if_empty(a["AddressLine2"]),
            "city": none_if_empty(a["City"]),
            "stateProvince": none_if_empty(a["StateProvince"]),
            "countryRegion": none_if_empty(a["CountryRegion"]),
            "postalCode": none_if_empty(a["PostalCode"]),
        }
    return lookup


def build_customer_address_map(customer_addresses: list[dict]) -> dict:
    """CustomerID → list of {addressId, addressType}."""
    mapping = {}
    for ca in customer_addresses:
        cid = int(ca["CustomerID"])
        aid = int(ca["AddressID"])
        entry = {"addressId": aid, "addressType": none_if_empty(ca["AddressType"])}
        mapping.setdefault(cid, []).append(entry)
    return mapping


def build_category_lookup(categories: list[dict]) -> dict:
    """ProductCategoryID → {name, parentId, parentName}."""
    cat_map = {}
    for c in categories:
        cid = int(c["ProductCategoryID"])
        cat_map[cid] = {
            "name": none_if_empty(c["Name"]),
            "parentId": parse_int(c["ParentProductCategoryID"]),
        }
    # Resolve parent names
    for cid, info in cat_map.items():
        pid = info["parentId"]
        info["parentName"] = cat_map[pid]["name"] if pid and pid in cat_map else None
    return cat_map


def build_model_lookup(models: list[dict]) -> dict:
    """ProductModelID → {name, catalogDescription}."""
    lookup = {}
    for m in models:
        mid = int(m["ProductModelID"])
        lookup[mid] = {
            "name": none_if_empty(m["Name"]),
            "catalogDescription": none_if_empty(m["CatalogDescription"]),
        }
    return lookup


def build_description_lookup(descriptions: list[dict]) -> dict:
    """ProductDescriptionID → description text."""
    return {int(d["ProductDescriptionID"]): none_if_empty(d["Description"]) for d in descriptions}


def build_model_descriptions_map(
    model_desc_junctions: list[dict], desc_lookup: dict
) -> dict:
    """ProductModelID → list of {culture, description}."""
    mapping = {}
    for j in model_desc_junctions:
        mid = int(j["ProductModelID"])
        did = int(j["ProductDescriptionID"])
        culture = j["Culture"].strip()
        desc_text = desc_lookup.get(did, "")
        mapping.setdefault(mid, []).append({"culture": culture, "description": desc_text})
    return mapping


def build_product_lookup(products: list[dict]) -> dict:
    """ProductID → {name, productNumber}."""
    return {
        int(p["ProductID"]): {
            "name": none_if_empty(p["Name"]),
            "productNumber": none_if_empty(p["ProductNumber"]),
        }
        for p in products
    }


# ---------------------------------------------------------------------------
# Convert: Customer documents
# ---------------------------------------------------------------------------

def convert_customers(
    customers: list[dict],
    cust_addr_map: dict,
    addr_lookup: dict,
) -> list[dict]:
    docs = []
    for c in customers:
        cid = int(c["CustomerID"])
        # Build embedded addresses
        addresses = []
        for ca in cust_addr_map.get(cid, []):
            addr = addr_lookup.get(ca["addressId"])
            if addr:
                embedded = dict(addr)
                embedded["addressType"] = ca["addressType"]
                addresses.append(embedded)

        doc = {
            "id": f"customer-{cid}",
            "type": "customer",
            "customerId": cid,
            "nameStyle": parse_bool(c["NameStyle"]),
            "title": none_if_empty(c["Title"]),
            "firstName": none_if_empty(c["FirstName"]),
            "middleName": none_if_empty(c["MiddleName"]),
            "lastName": none_if_empty(c["LastName"]),
            "suffix": none_if_empty(c["Suffix"]),
            "companyName": none_if_empty(c["CompanyName"]),
            "salesPerson": none_if_empty(c["SalesPerson"]),
            "emailAddress": none_if_empty(c["EmailAddress"]),
            "phone": none_if_empty(c["Phone"]),
            "addresses": addresses,
            "modifiedDate": parse_date(c["ModifiedDate"]),
        }
        docs.append(doc)
    return docs


# ---------------------------------------------------------------------------
# Convert: SalesOrder documents
# ---------------------------------------------------------------------------

def convert_sales_orders(
    headers: list[dict],
    details: list[dict],
    addr_lookup: dict,
    product_lookup: dict,
) -> list[dict]:
    # Group details by SalesOrderID
    details_by_order = {}
    for d in details:
        soid = int(d["SalesOrderID"])
        details_by_order.setdefault(soid, []).append(d)

    docs = []
    for h in headers:
        soid = int(h["SalesOrderID"])
        cid = int(h["CustomerID"])

        # Address snapshots
        ship_addr_id = parse_int(h["ShipToAddressID"])
        bill_addr_id = parse_int(h["BillToAddressID"])
        ship_addr = addr_lookup.get(ship_addr_id, {})
        bill_addr = addr_lookup.get(bill_addr_id, {})

        def to_snapshot(a: dict) -> dict:
            return {
                "addressLine1": a.get("addressLine1"),
                "addressLine2": a.get("addressLine2"),
                "city": a.get("city"),
                "stateProvince": a.get("stateProvince"),
                "countryRegion": a.get("countryRegion"),
                "postalCode": a.get("postalCode"),
            }

        # Order details
        order_details = []
        for d in details_by_order.get(soid, []):
            pid = int(d["ProductID"])
            prod = product_lookup.get(pid, {})
            qty = int(d["OrderQty"])
            unit_price = parse_decimal(d["UnitPrice"]) or 0.0
            discount = parse_decimal(d["UnitPriceDiscount"]) or 0.0
            line_total = round(unit_price * (1.0 - discount) * qty, 4)

            order_details.append({
                "salesOrderDetailId": int(d["SalesOrderDetailID"]),
                "productId": pid,
                "productName": prod.get("name"),
                "productNumber": prod.get("productNumber"),
                "orderQty": qty,
                "unitPrice": round(unit_price, 4),
                "unitPriceDiscount": round(discount, 4),
                "lineTotal": line_total,
            })

        sub_total = parse_decimal(h["SubTotal"]) or 0.0
        tax_amt = parse_decimal(h["TaxAmt"]) or 0.0
        freight = parse_decimal(h["Freight"]) or 0.0
        total_due = round(sub_total + tax_amt + freight, 4)

        doc = {
            "id": f"order-{soid}",
            "type": "salesOrder",
            "salesOrderId": soid,
            "customerId": cid,
            "revisionNumber": int(h["RevisionNumber"]),
            "orderDate": parse_date(h["OrderDate"]),
            "dueDate": parse_date(h["DueDate"]),
            "shipDate": parse_date(h["ShipDate"]),
            "status": int(h["Status"]),
            "onlineOrderFlag": parse_bool(h["OnlineOrderFlag"]),
            "salesOrderNumber": f"SO{soid}",
            "purchaseOrderNumber": none_if_empty(h["PurchaseOrderNumber"]),
            "accountNumber": none_if_empty(h["AccountNumber"]),
            "shipMethod": none_if_empty(h["ShipMethod"]),
            "creditCardApprovalCode": none_if_empty(h["CreditCardApprovalCode"]),
            "subTotal": round(sub_total, 4),
            "taxAmt": round(tax_amt, 4),
            "freight": round(freight, 4),
            "totalDue": total_due,
            "comment": none_if_empty(h["Comment"]),
            "shipToAddress": to_snapshot(ship_addr),
            "billToAddress": to_snapshot(bill_addr),
            "orderDetails": order_details,
            "modifiedDate": parse_date(h["ModifiedDate"]),
        }
        docs.append(doc)
    return docs


# ---------------------------------------------------------------------------
# Convert: Product documents
# ---------------------------------------------------------------------------

def convert_products(
    products: list[dict],
    cat_lookup: dict,
    model_lookup: dict,
    model_desc_map: dict,
) -> list[dict]:
    docs = []
    for p in products:
        pid = int(p["ProductID"])
        cat_id = parse_int(p["ProductCategoryID"])
        model_id = parse_int(p["ProductModelID"])

        cat_info = cat_lookup.get(cat_id, {})
        model_info = model_lookup.get(model_id, {}) if model_id else {}
        descriptions = model_desc_map.get(model_id, []) if model_id else []
        # Filter to only 'en' descriptions for products to keep documents lean
        # (all cultures are stored in the productModel document)
        en_descriptions = [d for d in descriptions if d["culture"] == "en"]

        doc = {
            "id": f"product-{pid}",
            "type": "product",
            "productId": pid,
            "productCategoryId": cat_id,
            "name": none_if_empty(p["Name"]),
            "productNumber": none_if_empty(p["ProductNumber"]),
            "color": none_if_empty(p["Color"]),
            "standardCost": parse_decimal(p["StandardCost"]),
            "listPrice": parse_decimal(p["ListPrice"]),
            "size": none_if_empty(p["Size"]),
            "weight": parse_decimal(p["Weight"]),
            "categoryName": cat_info.get("name"),
            "parentCategoryName": cat_info.get("parentName"),
            "productModelId": model_id,
            "productModelName": model_info.get("name"),
            "descriptions": en_descriptions,
            "sellStartDate": parse_date(p["SellStartDate"]),
            "sellEndDate": parse_date(p["SellEndDate"]),
            "discontinuedDate": parse_date(p["DiscontinuedDate"]),
            "thumbnailPhotoUrl": None,  # Binary moved to Blob Storage
            "thumbnailPhotoFileName": none_if_empty(p["ThumbnailPhotoFileName"]),
            "modifiedDate": parse_date(p["ModifiedDate"]),
        }
        docs.append(doc)
    return docs


# ---------------------------------------------------------------------------
# Convert: ProductCategory documents
# ---------------------------------------------------------------------------

def convert_product_categories(categories: list[dict], cat_lookup: dict) -> list[dict]:
    docs = []
    for c in categories:
        cid = int(c["ProductCategoryID"])
        info = cat_lookup[cid]
        doc = {
            "id": f"category-{cid}",
            "type": "productCategory",
            "productCategoryId": cid,
            "parentProductCategoryId": info["parentId"],
            "parentCategoryName": info["parentName"],
            "name": info["name"],
            "modifiedDate": parse_date(c["ModifiedDate"]),
        }
        docs.append(doc)
    return docs


# ---------------------------------------------------------------------------
# Convert: ProductModel documents
# ---------------------------------------------------------------------------

def convert_product_models(
    models: list[dict],
    model_desc_map: dict,
) -> list[dict]:
    docs = []
    for m in models:
        mid = int(m["ProductModelID"])
        descriptions = model_desc_map.get(mid, [])
        doc = {
            "id": f"model-{mid}",
            "type": "productModel",
            "productModelId": mid,
            "productCategoryId": 0,  # Synthetic PK for co-location
            "name": none_if_empty(m["Name"]),
            "catalogDescription": none_if_empty(m["CatalogDescription"]),
            "descriptions": descriptions,
            "modifiedDate": parse_date(m["ModifiedDate"]),
        }
        docs.append(doc)
    return docs


# ---------------------------------------------------------------------------
# Save JSON files
# ---------------------------------------------------------------------------

def save_json(docs: list[dict], filename: str):
    path = DATA_DIR / filename
    with open(path, "w", encoding="utf-8") as f:
        json.dump(docs, f, indent=2, ensure_ascii=False)
    print(f"  Saved {len(docs):>5} docs → {path.relative_to(PROJECT_ROOT)}")


# ---------------------------------------------------------------------------
# Generate all JSON documents
# ---------------------------------------------------------------------------

def generate_all():
    print("=" * 60)
    print("Phase 1: Reading CSV source files...")
    print("=" * 60)

    customers_raw = read_csv("Customer.csv")
    addresses_raw = read_csv("Address.csv")
    cust_addr_raw = read_csv("CustomerAddress.csv")
    orders_raw = read_csv("SalesOrderHeader.csv")
    details_raw = read_csv("SalesOrderDetail.csv")
    products_raw = read_csv("Product.csv")
    categories_raw = read_csv("ProductCategory.csv")
    models_raw = read_csv("ProductModel.csv")
    descriptions_raw = read_csv("ProductDescription.csv")
    model_desc_raw = read_csv("ProductModelProductDescription.csv")

    print(f"  Customer:         {len(customers_raw):>5} rows")
    print(f"  Address:          {len(addresses_raw):>5} rows")
    print(f"  CustomerAddress:  {len(cust_addr_raw):>5} rows")
    print(f"  SalesOrderHeader: {len(orders_raw):>5} rows")
    print(f"  SalesOrderDetail: {len(details_raw):>5} rows")
    print(f"  Product:          {len(products_raw):>5} rows")
    print(f"  ProductCategory:  {len(categories_raw):>5} rows")
    print(f"  ProductModel:     {len(models_raw):>5} rows")
    print(f"  ProductDescription: {len(descriptions_raw):>3} rows")
    print(f"  ModelProdDesc:    {len(model_desc_raw):>5} rows")

    print("\n" + "=" * 60)
    print("Phase 2: Building lookup tables...")
    print("=" * 60)

    addr_lookup = build_address_lookup(addresses_raw)
    cust_addr_map = build_customer_address_map(cust_addr_raw)
    cat_lookup = build_category_lookup(categories_raw)
    model_lookup = build_model_lookup(models_raw)
    desc_lookup = build_description_lookup(descriptions_raw)
    model_desc_map = build_model_descriptions_map(model_desc_raw, desc_lookup)
    product_lookup = build_product_lookup(products_raw)
    print("  Lookups built successfully.")

    print("\n" + "=" * 60)
    print("Phase 3: Converting to Cosmos DB documents...")
    print("=" * 60)

    # customer-orders container
    customer_docs = convert_customers(customers_raw, cust_addr_map, addr_lookup)
    order_docs = convert_sales_orders(orders_raw, details_raw, addr_lookup, product_lookup)

    # product-catalog container
    product_docs = convert_products(products_raw, cat_lookup, model_lookup, model_desc_map)
    category_docs = convert_product_categories(categories_raw, cat_lookup)
    model_docs = convert_product_models(models_raw, model_desc_map)

    print(f"  customer docs:        {len(customer_docs):>5}")
    print(f"  salesOrder docs:      {len(order_docs):>5}")
    print(f"  product docs:         {len(product_docs):>5}")
    print(f"  productCategory docs: {len(category_docs):>5}")
    print(f"  productModel docs:    {len(model_docs):>5}")
    total = len(customer_docs) + len(order_docs) + len(product_docs) + len(category_docs) + len(model_docs)
    print(f"  TOTAL:                {total:>5}")

    print("\n" + "=" * 60)
    print("Phase 4: Saving JSON files to DataMigration/data/...")
    print("=" * 60)

    DATA_DIR.mkdir(parents=True, exist_ok=True)

    # Per-entity files
    save_json(customer_docs, "customers.json")
    save_json(order_docs, "sales-orders.json")
    save_json(product_docs, "products.json")
    save_json(category_docs, "product-categories.json")
    save_json(model_docs, "product-models.json")

    # Per-container combined files (for bulk loading)
    customer_orders_all = customer_docs + order_docs
    save_json(customer_orders_all, "customer-orders-container.json")

    product_catalog_all = product_docs + category_docs + model_docs
    save_json(product_catalog_all, "product-catalog-container.json")

    print(f"\n  Total documents for customer-orders: {len(customer_orders_all)}")
    print(f"  Total documents for product-catalog: {len(product_catalog_all)}")

    return customer_orders_all, product_catalog_all


# ---------------------------------------------------------------------------
# Load into Cosmos DB
# ---------------------------------------------------------------------------

def load_to_cosmos(customer_orders_docs=None, product_catalog_docs=None):
    from azure.cosmos import CosmosClient, PartitionKey, exceptions
    from azure.identity import DefaultAzureCredential

    print("\n" + "=" * 60)
    print("Phase 5: Loading documents into Cosmos DB...")
    print("=" * 60)

    # Read from saved files if not passed in
    if customer_orders_docs is None:
        with open(DATA_DIR / "customer-orders-container.json", "r", encoding="utf-8") as f:
            customer_orders_docs = json.load(f)
    if product_catalog_docs is None:
        with open(DATA_DIR / "product-catalog-container.json", "r", encoding="utf-8") as f:
            product_catalog_docs = json.load(f)

    # Connect using Entra ID (DefaultAzureCredential)
    credential = DefaultAzureCredential()
    client = CosmosClient(COSMOS_ENDPOINT, credential=credential)
    database = client.get_database_client(DATABASE_NAME)

    # Load customer-orders
    container_co = database.get_container_client(CUSTOMER_ORDERS_CONTAINER)
    print(f"\n  Loading {len(customer_orders_docs)} docs into '{CUSTOMER_ORDERS_CONTAINER}'...")
    success_co, fail_co = 0, 0
    for doc in customer_orders_docs:
        try:
            container_co.upsert_item(doc)
            success_co += 1
        except Exception as e:
            fail_co += 1
            print(f"    ERROR [{doc['id']}]: {e}")
    print(f"    Success: {success_co}, Failed: {fail_co}")

    # Load product-catalog
    container_pc = database.get_container_client(PRODUCT_CATALOG_CONTAINER)
    print(f"\n  Loading {len(product_catalog_docs)} docs into '{PRODUCT_CATALOG_CONTAINER}'...")
    success_pc, fail_pc = 0, 0
    for doc in product_catalog_docs:
        try:
            container_pc.upsert_item(doc)
            success_pc += 1
        except Exception as e:
            fail_pc += 1
            print(f"    ERROR [{doc['id']}]: {e}")
    print(f"    Success: {success_pc}, Failed: {fail_pc}")

    total_success = success_co + success_pc
    total_fail = fail_co + fail_pc
    print(f"\n  TOTAL: {total_success} loaded, {total_fail} failed")
    return total_fail == 0


# ---------------------------------------------------------------------------
# Validate migration
# ---------------------------------------------------------------------------

def validate_migration():
    from azure.cosmos import CosmosClient
    from azure.identity import DefaultAzureCredential

    print("\n" + "=" * 60)
    print("Phase 6: Validating data migration...")
    print("=" * 60)

    credential = DefaultAzureCredential()
    client = CosmosClient(COSMOS_ENDPOINT, credential=credential)
    database = client.get_database_client(DATABASE_NAME)

    # Expected counts from CSV
    expected = {
        "customer-orders": {
            "customer": 847,
            "salesOrder": 32,
        },
        "product-catalog": {
            "product": 295,
            "productCategory": 41,
            "productModel": 128,
        },
    }

    all_passed = True

    for container_name, type_counts in expected.items():
        container = database.get_container_client(container_name)
        print(f"\n  Container: {container_name}")

        # Count total docs
        total_query = "SELECT VALUE COUNT(1) FROM c"
        total_count = list(container.query_items(total_query, enable_cross_partition_query=True))[0]
        expected_total = sum(type_counts.values())
        status = "PASS" if total_count == expected_total else "FAIL"
        if status == "FAIL":
            all_passed = False
        print(f"    Total documents: {total_count} (expected {expected_total}) [{status}]")

        # Count by type
        for doc_type, expected_count in type_counts.items():
            type_query = f"SELECT VALUE COUNT(1) FROM c WHERE c.type = '{doc_type}'"
            actual_count = list(container.query_items(type_query, enable_cross_partition_query=True))[0]
            status = "PASS" if actual_count == expected_count else "FAIL"
            if status == "FAIL":
                all_passed = False
            print(f"    type='{doc_type}': {actual_count} (expected {expected_count}) [{status}]")

    # Spot-check: customer-1 should have embedded addresses
    print("\n  Spot-check validations:")
    co_container = database.get_container_client(CUSTOMER_ORDERS_CONTAINER)

    # Check customer-1 (Orlando Gee)
    try:
        cust1 = co_container.read_item("customer-1", partition_key=1)
        checks = [
            ("customer-1 exists", True),
            ("customer-1 type=customer", cust1.get("type") == "customer"),
            ("customer-1 firstName=Orlando", cust1.get("firstName") == "Orlando"),
            ("customer-1 has addresses[]", isinstance(cust1.get("addresses"), list)),
            ("customer-1 no passwordHash", "passwordHash" not in cust1),
        ]
        for desc, passed in checks:
            status = "PASS" if passed else "FAIL"
            if not passed:
                all_passed = False
            print(f"    {desc}: [{status}]")
    except Exception as e:
        print(f"    customer-1 read FAILED: {e}")
        all_passed = False

    # Check an order (first one from CSV - 71774)
    try:
        order = co_container.read_item("order-71774", partition_key=29847)
        checks = [
            ("order-71774 exists", True),
            ("order-71774 type=salesOrder", order.get("type") == "salesOrder"),
            ("order-71774 has orderDetails[]", isinstance(order.get("orderDetails"), list) and len(order["orderDetails"]) > 0),
            ("order-71774 has shipToAddress", order.get("shipToAddress") is not None),
            ("order-71774 salesOrderNumber=SO71774", order.get("salesOrderNumber") == "SO71774"),
            ("order-71774 detail has productName", order["orderDetails"][0].get("productName") is not None),
        ]
        for desc, passed in checks:
            status = "PASS" if passed else "FAIL"
            if not passed:
                all_passed = False
            print(f"    {desc}: [{status}]")
    except Exception as e:
        print(f"    order-71774 read FAILED: {e}")
        all_passed = False

    # Check product-catalog: product-680 (HL Road Frame - Black, 58)
    pc_container = database.get_container_client(PRODUCT_CATALOG_CONTAINER)
    try:
        prod = pc_container.read_item("product-680", partition_key=18)
        checks = [
            ("product-680 exists", True),
            ("product-680 type=product", prod.get("type") == "product"),
            ("product-680 categoryName=Road Frames", prod.get("categoryName") == "Road Frames"),
            ("product-680 productModelName=HL Road Frame", prod.get("productModelName") == "HL Road Frame"),
            ("product-680 has descriptions[]", isinstance(prod.get("descriptions"), list)),
        ]
        for desc, passed in checks:
            status = "PASS" if passed else "FAIL"
            if not passed:
                all_passed = False
            print(f"    {desc}: [{status}]")
    except Exception as e:
        print(f"    product-680 read FAILED: {e}")
        all_passed = False

    # Check category-18 (Road Frames)
    try:
        cat = pc_container.read_item("category-18", partition_key=18)
        checks = [
            ("category-18 exists", True),
            ("category-18 type=productCategory", cat.get("type") == "productCategory"),
            ("category-18 name=Road Frames", cat.get("name") == "Road Frames"),
            ("category-18 parentCategoryName=Components", cat.get("parentCategoryName") == "Components"),
        ]
        for desc, passed in checks:
            status = "PASS" if passed else "FAIL"
            if not passed:
                all_passed = False
            print(f"    {desc}: [{status}]")
    except Exception as e:
        print(f"    category-18 read FAILED: {e}")
        all_passed = False

    # Check model-6 (HL Road Frame) in partition 0
    try:
        model = pc_container.read_item("model-6", partition_key=0)
        checks = [
            ("model-6 exists", True),
            ("model-6 type=productModel", model.get("type") == "productModel"),
            ("model-6 productCategoryId=0", model.get("productCategoryId") == 0),
            ("model-6 name=HL Road Frame", model.get("name") == "HL Road Frame"),
            ("model-6 has descriptions[]", isinstance(model.get("descriptions"), list) and len(model["descriptions"]) > 0),
        ]
        for desc, passed in checks:
            status = "PASS" if passed else "FAIL"
            if not passed:
                all_passed = False
            print(f"    {desc}: [{status}]")
    except Exception as e:
        print(f"    model-6 read FAILED: {e}")
        all_passed = False

    print("\n" + "=" * 60)
    if all_passed:
        print("VALIDATION RESULT: ALL CHECKS PASSED ✓")
    else:
        print("VALIDATION RESULT: SOME CHECKS FAILED ✗")
    print("=" * 60)

    return all_passed


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="AdventureWorks CSV → Cosmos DB migration tool")
    parser.add_argument("--generate", action="store_true", help="Generate JSON documents from CSV files")
    parser.add_argument("--load", action="store_true", help="Load JSON documents into Cosmos DB")
    parser.add_argument("--validate", action="store_true", help="Validate data migration in Cosmos DB")
    parser.add_argument("--all", action="store_true", help="Run all phases: generate, load, validate")
    args = parser.parse_args()

    if not any([args.generate, args.load, args.validate, args.all]):
        args.all = True  # Default to all phases

    co_docs, pc_docs = None, None

    if args.generate or args.all:
        co_docs, pc_docs = generate_all()

    if args.load or args.all:
        load_to_cosmos(co_docs, pc_docs)

    if args.validate or args.all:
        validate_migration()


if __name__ == "__main__":
    main()
