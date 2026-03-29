"""
CSV-to-Cosmos DB JSON Converter
Reads AdventureWorksLT CSV files and produces target JSON documents
following the schema defined in schema_and_access_patterns_conversion_plan.md.

Output:
  DataMigration/data/customer-orders/  -> customer docs + salesOrder docs
  DataMigration/data/product-catalog/  -> product docs + category docs + model docs
"""

import csv
import json
import os
import re
from datetime import datetime
from pathlib import Path

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
CSV_DIR = PROJECT_ROOT / "AdventureWorksLT" / "AdventureWorksLT"
DATA_DIR = SCRIPT_DIR.parent / "data"

CUSTOMER_ORDERS_DIR = DATA_DIR / "customer-orders"
PRODUCT_CATALOG_DIR = DATA_DIR / "product-catalog"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def parse_decimal(value: str) -> float | None:
    """Parse a decimal value that may use comma as decimal separator."""
    if value is None or value.strip() == "":
        return None
    # CSV fields like "880,3484" use comma as decimal separator (European format)
    return round(float(value.replace(",", ".")), 4)


def parse_int(value: str) -> int | None:
    if value is None or value.strip() == "":
        return None
    return int(value)


def parse_bool(value: str) -> bool:
    if value is None or value.strip() == "":
        return False
    return value.strip().lower() in ("true", "1", "yes")


def to_iso8601(value: str) -> str | None:
    """Convert SQL datetime string to ISO 8601."""
    if value is None or value.strip() == "":
        return None
    # Format: "2008-06-01 00:00:00.000"
    dt = datetime.strptime(value.strip(), "%Y-%m-%d %H:%M:%S.%f")
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


def nullable_str(value: str) -> str | None:
    if value is None or value.strip() == "":
        return None
    return value.strip()


def read_csv(filename: str) -> list[dict]:
    """Read a CSV file and return list of row dicts."""
    filepath = CSV_DIR / filename
    rows = []
    with open(filepath, "r", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for row in reader:
            rows.append(row)
    return rows


def write_json(directory: Path, filename: str, documents: list[dict]):
    """Write documents to a JSON file."""
    os.makedirs(directory, exist_ok=True)
    filepath = directory / filename
    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(documents, f, indent=2, ensure_ascii=False)
    print(f"  Wrote {len(documents)} documents to {filepath.relative_to(DATA_DIR.parent)}")


# ---------------------------------------------------------------------------
# Load all CSV data into memory
# ---------------------------------------------------------------------------
def load_all_csv():
    """Load all CSV files into lookup structures."""
    data = {}
    data["customers"] = read_csv("Customer.csv")
    data["addresses"] = read_csv("Address.csv")
    data["customer_addresses"] = read_csv("CustomerAddress.csv")
    data["sales_order_headers"] = read_csv("SalesOrderHeader.csv")
    data["sales_order_details"] = read_csv("SalesOrderDetail.csv")
    data["products"] = read_csv("Product.csv")
    data["product_categories"] = read_csv("ProductCategory.csv")
    data["product_models"] = read_csv("ProductModel.csv")
    data["product_descriptions"] = read_csv("ProductDescription.csv")
    data["product_model_descriptions"] = read_csv("ProductModelProductDescription.csv")
    return data


# ---------------------------------------------------------------------------
# Build lookup indices
# ---------------------------------------------------------------------------
def build_lookups(data: dict) -> dict:
    lookups = {}

    # Address by AddressID
    lookups["address_by_id"] = {
        row["AddressID"]: row for row in data["addresses"]
    }

    # CustomerAddress: CustomerID -> list of {AddressID, AddressType}
    ca_map: dict[str, list] = {}
    for row in data["customer_addresses"]:
        cid = row["CustomerID"]
        if cid not in ca_map:
            ca_map[cid] = []
        ca_map[cid].append(row)
    lookups["customer_addresses"] = ca_map

    # SalesOrderDetail: SalesOrderID -> list of details
    detail_map: dict[str, list] = {}
    for row in data["sales_order_details"]:
        soid = row["SalesOrderID"]
        if soid not in detail_map:
            detail_map[soid] = []
        detail_map[soid].append(row)
    lookups["order_details"] = detail_map

    # Product by ProductID
    lookups["product_by_id"] = {
        row["ProductID"]: row for row in data["products"]
    }

    # ProductCategory by ProductCategoryID
    lookups["category_by_id"] = {
        row["ProductCategoryID"]: row for row in data["product_categories"]
    }

    # ProductModel by ProductModelID
    lookups["model_by_id"] = {
        row["ProductModelID"]: row for row in data["product_models"]
    }

    # ProductDescription by ProductDescriptionID
    lookups["description_by_id"] = {
        row["ProductDescriptionID"]: row for row in data["product_descriptions"]
    }

    # ProductModelProductDescription: ProductModelID -> list of {DescriptionID, Culture}
    pmpd_map: dict[str, list] = {}
    for row in data["product_model_descriptions"]:
        mid = row["ProductModelID"]
        if mid not in pmpd_map:
            pmpd_map[mid] = []
        pmpd_map[mid].append(row)
    lookups["model_descriptions"] = pmpd_map

    return lookups


# ===========================================================================
# Transform: customer-orders container
# ===========================================================================
def transform_customers(data: dict, lookups: dict) -> list[dict]:
    """Transform Customer CSV rows into customer documents with embedded addresses."""
    docs = []
    for row in data["customers"]:
        cid = row["CustomerID"]

        # Build embedded addresses
        addresses = []
        for ca in lookups["customer_addresses"].get(cid, []):
            addr_row = lookups["address_by_id"].get(ca["AddressID"])
            if addr_row:
                addresses.append({
                    "addressId": parse_int(addr_row["AddressID"]),
                    "addressType": nullable_str(ca["AddressType"]),
                    "addressLine1": nullable_str(addr_row["AddressLine1"]),
                    "addressLine2": nullable_str(addr_row["AddressLine2"]),
                    "city": nullable_str(addr_row["City"]),
                    "stateProvince": nullable_str(addr_row["StateProvince"]),
                    "countryRegion": nullable_str(addr_row["CountryRegion"]),
                    "postalCode": nullable_str(addr_row["PostalCode"]),
                })

        doc = {
            "id": f"customer-{cid}",
            "customerId": parse_int(cid),
            "type": "customer",
            "nameStyle": parse_bool(row["NameStyle"]),
            "title": nullable_str(row["Title"]),
            "firstName": nullable_str(row["FirstName"]),
            "middleName": nullable_str(row["MiddleName"]),
            "lastName": nullable_str(row["LastName"]),
            "suffix": nullable_str(row["Suffix"]),
            "companyName": nullable_str(row["CompanyName"]),
            "salesPerson": nullable_str(row["SalesPerson"]),
            "emailAddress": nullable_str(row["EmailAddress"]),
            "phone": nullable_str(row["Phone"]),
            "passwordHash": nullable_str(row["PasswordHash"]),
            "passwordSalt": nullable_str(row["PasswordSalt"]),
            "addresses": addresses,
            "modifiedDate": to_iso8601(row["ModifiedDate"]),
            "ttl": -1,
            "_schemaVersion": 1,
        }
        docs.append(doc)
    return docs


def transform_sales_orders(data: dict, lookups: dict) -> list[dict]:
    """Transform SalesOrderHeader CSV rows into salesOrder documents with embedded details."""
    docs = []
    for row in data["sales_order_headers"]:
        soid = row["SalesOrderID"]
        cid = row["CustomerID"]

        # Build embedded order details
        details = []
        for det in lookups["order_details"].get(soid, []):
            product_row = lookups["product_by_id"].get(det["ProductID"])
            product_name = nullable_str(product_row["Name"]) if product_row else None
            product_number = nullable_str(product_row["ProductNumber"]) if product_row else None

            unit_price = parse_decimal(det["UnitPrice"])
            discount = parse_decimal(det["UnitPriceDiscount"])
            qty = parse_int(det["OrderQty"])

            # Compute lineTotal per transform rule 13
            if unit_price is not None and discount is not None and qty is not None:
                line_total = round(unit_price * (1 - discount) * qty, 4)
            else:
                line_total = parse_decimal(det["LineTotal"])

            details.append({
                "salesOrderDetailId": parse_int(det["SalesOrderDetailID"]),
                "productId": parse_int(det["ProductID"]),
                "productName": product_name,
                "productNumber": product_number,
                "orderQty": qty,
                "unitPrice": unit_price,
                "unitPriceDiscount": discount,
                "lineTotal": line_total,
            })

        # Snapshot ship-to and bill-to addresses
        ship_addr = lookups["address_by_id"].get(row["ShipToAddressID"])
        bill_addr = lookups["address_by_id"].get(row["BillToAddressID"])

        def addr_snapshot(addr_row):
            if not addr_row:
                return None
            return {
                "addressLine1": nullable_str(addr_row["AddressLine1"]),
                "addressLine2": nullable_str(addr_row["AddressLine2"]),
                "city": nullable_str(addr_row["City"]),
                "stateProvince": nullable_str(addr_row["StateProvince"]),
                "countryRegion": nullable_str(addr_row["CountryRegion"]),
                "postalCode": nullable_str(addr_row["PostalCode"]),
            }

        sub_total = parse_decimal(row["SubTotal"])
        tax_amt = parse_decimal(row["TaxAmt"])
        freight = parse_decimal(row["Freight"])

        # Compute totalDue per transform rule 12
        if sub_total is not None and tax_amt is not None and freight is not None:
            total_due = round(sub_total + tax_amt + freight, 4)
        else:
            total_due = parse_decimal(row["TotalDue"])

        # Compute salesOrderNumber per transform rule 11
        sales_order_number = f"SO{soid}"

        doc = {
            "id": f"order-{soid}",
            "salesOrderId": parse_int(soid),
            "customerId": parse_int(cid),
            "type": "salesOrder",
            "revisionNumber": parse_int(row["RevisionNumber"]),
            "orderDate": to_iso8601(row["OrderDate"]),
            "dueDate": to_iso8601(row["DueDate"]),
            "shipDate": to_iso8601(row["ShipDate"]),
            "status": parse_int(row["Status"]),
            "onlineOrderFlag": parse_bool(row["OnlineOrderFlag"]),
            "salesOrderNumber": sales_order_number,
            "purchaseOrderNumber": nullable_str(row["PurchaseOrderNumber"]),
            "accountNumber": nullable_str(row["AccountNumber"]),
            "shipMethod": nullable_str(row["ShipMethod"]),
            "creditCardApprovalCode": nullable_str(row["CreditCardApprovalCode"]),
            "subTotal": sub_total,
            "taxAmt": tax_amt,
            "freight": freight,
            "totalDue": total_due,
            "comment": nullable_str(row["Comment"]),
            "shipToAddress": addr_snapshot(ship_addr),
            "billToAddress": addr_snapshot(bill_addr),
            "details": details,
            "modifiedDate": to_iso8601(row["ModifiedDate"]),
            "ttl": 63072000,
            "_schemaVersion": 1,
        }
        docs.append(doc)
    return docs


# ===========================================================================
# Transform: product-catalog container
# ===========================================================================
def transform_categories(data: dict, lookups: dict) -> list[dict]:
    """Transform ProductCategory CSV rows into category documents."""
    docs = []
    for row in data["product_categories"]:
        cat_id = row["ProductCategoryID"]
        parent_id = nullable_str(row["ParentProductCategoryID"])

        # Denormalize parent name (transform rule 21)
        parent_name = None
        if parent_id:
            parent_row = lookups["category_by_id"].get(parent_id)
            if parent_row:
                parent_name = nullable_str(parent_row["Name"])

        doc = {
            "id": f"category-{cat_id}",
            "partitionKey": f"category-{cat_id}",
            "productCategoryId": parse_int(cat_id),
            "type": "category",
            "name": nullable_str(row["Name"]),
            "parentProductCategoryId": parse_int(parent_id) if parent_id else None,
            "parentCategoryName": parent_name,
            "modifiedDate": to_iso8601(row["ModifiedDate"]),
            "_schemaVersion": 1,
        }
        docs.append(doc)
    return docs


def transform_models(data: dict, lookups: dict) -> list[dict]:
    """Transform ProductModel CSV rows into model documents with embedded descriptions."""
    docs = []
    for row in data["product_models"]:
        mid = row["ProductModelID"]

        # Embed descriptions (transform rules 20)
        descriptions = []
        for pmpd in lookups["model_descriptions"].get(mid, []):
            desc_row = lookups["description_by_id"].get(pmpd["ProductDescriptionID"])
            if desc_row:
                descriptions.append({
                    "culture": pmpd["Culture"].strip(),
                    "description": nullable_str(desc_row["Description"]),
                })

        # CatalogDescription: XML -> JSON (transform rule 10)
        # Most rows have empty CatalogDescription; store as null or simple object
        catalog_desc_raw = nullable_str(row.get("CatalogDescription", ""))
        catalog_description = None
        if catalog_desc_raw:
            # Simple extraction — the XML is complex; store a summary
            catalog_description = {"raw": catalog_desc_raw}

        doc = {
            "id": f"model-{mid}",
            "partitionKey": f"model-{mid}",
            "productModelId": parse_int(mid),
            "type": "model",
            "name": nullable_str(row["Name"]),
            "catalogDescription": catalog_description,
            "descriptions": descriptions,
            "modifiedDate": to_iso8601(row["ModifiedDate"]),
            "_schemaVersion": 1,
        }
        docs.append(doc)
    return docs


def transform_products(data: dict, lookups: dict) -> list[dict]:
    """Transform Product CSV rows into product documents with embedded category/model snapshots."""
    docs = []
    for row in data["products"]:
        pid = row["ProductID"]
        cat_id = nullable_str(row["ProductCategoryID"])
        model_id = nullable_str(row["ProductModelID"])

        # Embed category snapshot (transform rule 18)
        category_snapshot = None
        if cat_id:
            cat_row = lookups["category_by_id"].get(cat_id)
            if cat_row:
                parent_id = nullable_str(cat_row["ParentProductCategoryID"])
                parent_name = None
                if parent_id:
                    parent_row = lookups["category_by_id"].get(parent_id)
                    if parent_row:
                        parent_name = nullable_str(parent_row["Name"])
                category_snapshot = {
                    "productCategoryId": parse_int(cat_id),
                    "name": nullable_str(cat_row["Name"]),
                    "parentCategoryName": parent_name,
                }

        # Embed model snapshot (transform rule 19)
        model_snapshot = None
        if model_id:
            model_row = lookups["model_by_id"].get(model_id)
            if model_row:
                model_snapshot = {
                    "productModelId": parse_int(model_id),
                    "name": nullable_str(model_row["Name"]),
                }

        # ThumbNailPhoto -> URL (transform rule 22)
        photo_filename = nullable_str(row.get("ThumbnailPhotoFileName", ""))
        if photo_filename:
            thumbnail_url = f"https://adventureworksstorage.blob.core.windows.net/product-images/{photo_filename}"
        else:
            thumbnail_url = None

        doc = {
            "id": f"product-{pid}",
            "partitionKey": f"product-{pid}",
            "productId": parse_int(pid),
            "type": "product",
            "name": nullable_str(row["Name"]),
            "productNumber": nullable_str(row["ProductNumber"]),
            "color": nullable_str(row["Color"]),
            "standardCost": parse_decimal(row["StandardCost"]),
            "listPrice": parse_decimal(row["ListPrice"]),
            "size": nullable_str(row["Size"]),
            "weight": parse_decimal(row.get("Weight", "")),
            "productCategoryId": parse_int(cat_id) if cat_id else None,
            "category": category_snapshot,
            "productModelId": parse_int(model_id) if model_id else None,
            "model": model_snapshot,
            "sellStartDate": to_iso8601(row["SellStartDate"]),
            "sellEndDate": to_iso8601(row.get("SellEndDate", "")),
            "discontinuedDate": to_iso8601(row.get("DiscontinuedDate", "")),
            "thumbnailPhotoUrl": thumbnail_url,
            "modifiedDate": to_iso8601(row["ModifiedDate"]),
            "_schemaVersion": 1,
        }
        docs.append(doc)
    return docs


# ===========================================================================
# Main
# ===========================================================================
def main():
    print("=" * 60)
    print("AdventureWorksLT CSV → Cosmos DB JSON Converter")
    print("=" * 60)

    print("\n[1/2] Loading CSV data...")
    data = load_all_csv()
    for key, rows in data.items():
        print(f"  {key}: {len(rows)} rows")

    print("\n[2/2] Building lookups and transforming...")
    lookups = build_lookups(data)

    # --- customer-orders container ---
    print("\n--- Container: customer-orders ---")
    customer_docs = transform_customers(data, lookups)
    write_json(CUSTOMER_ORDERS_DIR, "customers.json", customer_docs)

    sales_order_docs = transform_sales_orders(data, lookups)
    write_json(CUSTOMER_ORDERS_DIR, "sales-orders.json", sales_order_docs)

    # --- product-catalog container ---
    print("\n--- Container: product-catalog ---")
    category_docs = transform_categories(data, lookups)
    write_json(PRODUCT_CATALOG_DIR, "categories.json", category_docs)

    model_docs = transform_models(data, lookups)
    write_json(PRODUCT_CATALOG_DIR, "models.json", model_docs)

    product_docs = transform_products(data, lookups)
    write_json(PRODUCT_CATALOG_DIR, "products.json", product_docs)

    # --- Summary ---
    print("\n" + "=" * 60)
    print("Document Count Summary")
    print("=" * 60)
    print(f"  customer-orders container:")
    print(f"    customer documents:   {len(customer_docs):>6}  (expected: 847)")
    print(f"    salesOrder documents: {len(sales_order_docs):>6}  (expected: 32)")

    # Count embedded details
    total_embedded_details = sum(len(d["details"]) for d in sales_order_docs)
    print(f"    embedded details:     {total_embedded_details:>6}  (expected: 542)")

    # Count embedded addresses
    total_embedded_addresses = sum(len(d["addresses"]) for d in customer_docs)
    print(f"    embedded addresses:   {total_embedded_addresses:>6}  (expected: 417)")

    print(f"\n  product-catalog container:")
    print(f"    category documents:   {len(category_docs):>6}  (expected: 41)")
    print(f"    model documents:      {len(model_docs):>6}  (expected: 165)")
    print(f"    product documents:    {len(product_docs):>6}  (expected: 295)")

    # Count embedded descriptions
    total_embedded_descs = sum(len(d["descriptions"]) for d in model_docs)
    print(f"    embedded descriptions:{total_embedded_descs:>6}  (expected: 762)")

    total_docs = len(customer_docs) + len(sales_order_docs) + len(category_docs) + len(model_docs) + len(product_docs)
    print(f"\n  TOTAL documents:        {total_docs:>6}")
    print("=" * 60)

    return {
        "customers": len(customer_docs),
        "salesOrders": len(sales_order_docs),
        "embeddedDetails": total_embedded_details,
        "categories": len(category_docs),
        "models": len(model_docs),
        "products": len(product_docs),
    }


if __name__ == "__main__":
    main()
