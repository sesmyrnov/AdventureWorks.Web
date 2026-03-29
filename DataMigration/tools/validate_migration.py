"""
Validate Cosmos DB document counts against expected volumetrics.

Runs the validation queries from section 8.3 of the conversion plan
to confirm the migration loaded all data correctly.
"""

import json
import sys
from azure.cosmos import CosmosClient, exceptions
from azure.identity import DefaultAzureCredential

COSMOS_ENDPOINT = "https://ssm-cosmos-adwlt02.documents.azure.com:443/"
DATABASE_NAME = "adwkslt"

# Expected counts from source CSV data
EXPECTED = {
    "customers": 847,
    "salesOrders": 32,
    "embeddedDetails": 542,
    "products": 295,
    "categories": 41,
    "models": 128,  # Actual CSV has 128 rows (volumetrics estimated 165)
}


def run_query(container, query: str) -> list:
    """Run a cross-partition query and return all results."""
    results = list(container.query_items(
        query=query,
        enable_cross_partition_query=True,
    ))
    return results


def main():
    print("=" * 60)
    print("Cosmos DB Migration Validation")
    print(f"Endpoint: {COSMOS_ENDPOINT}")
    print(f"Database: {DATABASE_NAME}")
    print("=" * 60)

    credential = DefaultAzureCredential()
    client = CosmosClient(COSMOS_ENDPOINT, credential=credential)
    database = client.get_database_client(DATABASE_NAME)

    customer_orders = database.get_container_client("customer-orders")
    product_catalog = database.get_container_client("product-catalog")

    all_pass = True

    def check(label: str, actual: int, expected: int):
        nonlocal all_pass
        status = "PASS" if actual == expected else "FAIL"
        if status == "FAIL":
            all_pass = False
        print(f"  [{status}] {label}: {actual} (expected {expected})")

    # --- customer-orders container ---
    print("\n--- Container: customer-orders ---")

    # Customer count
    r = run_query(customer_orders, "SELECT VALUE COUNT(1) FROM c WHERE c.type = 'customer'")
    check("Customer count", r[0], EXPECTED["customers"])

    # SalesOrder count
    r = run_query(customer_orders, "SELECT VALUE COUNT(1) FROM c WHERE c.type = 'salesOrder'")
    check("SalesOrder count", r[0], EXPECTED["salesOrders"])

    # Embedded order details count
    r = run_query(customer_orders,
        "SELECT VALUE COUNT(1) FROM c JOIN d IN c.details WHERE c.type = 'salesOrder'")
    check("Embedded details count", r[0], EXPECTED["embeddedDetails"])

    # Verify computed fields: totalDue = subTotal + taxAmt + freight
    r = run_query(customer_orders,
        "SELECT VALUE COUNT(1) FROM c WHERE c.type = 'salesOrder' "
        "AND ABS(c.totalDue - (c.subTotal + c.taxAmt + c.freight)) > 0.01")
    check("Computed totalDue accuracy (mismatches)", r[0], 0)

    # No orphaned orders (all order customerIds exist as customer docs)
    r = run_query(customer_orders,
        "SELECT DISTINCT VALUE c.customerId FROM c WHERE c.type = 'salesOrder'")
    order_customer_ids = set(r)

    r2 = run_query(customer_orders,
        "SELECT DISTINCT VALUE c.customerId FROM c WHERE c.type = 'customer'")
    customer_ids = set(r2)

    orphaned = order_customer_ids - customer_ids
    check("Orphaned orders (customerIds not in customers)", len(orphaned), 0)

    # Spot-check: order 71774 detail count
    r = run_query(customer_orders,
        "SELECT VALUE ARRAY_LENGTH(c.details) FROM c "
        "WHERE c.type = 'salesOrder' AND c.salesOrderId = 71774")
    if r:
        print(f"  [INFO] Order 71774 has {r[0]} embedded details")

    # Spot-check: customer with addresses
    r = run_query(customer_orders,
        "SELECT TOP 1 c.customerId, ARRAY_LENGTH(c.addresses) AS addrCount "
        "FROM c WHERE c.type = 'customer' AND ARRAY_LENGTH(c.addresses) > 0")
    if r:
        print(f"  [INFO] Customer {r[0]['customerId']} has {r[0]['addrCount']} address(es)")

    # --- product-catalog container ---
    print("\n--- Container: product-catalog ---")

    # Product count
    r = run_query(product_catalog, "SELECT VALUE COUNT(1) FROM c WHERE c.type = 'product'")
    check("Product count", r[0], EXPECTED["products"])

    # Category count
    r = run_query(product_catalog, "SELECT VALUE COUNT(1) FROM c WHERE c.type = 'category'")
    check("Category count", r[0], EXPECTED["categories"])

    # Model count
    r = run_query(product_catalog, "SELECT VALUE COUNT(1) FROM c WHERE c.type = 'model'")
    check("Model count", r[0], EXPECTED["models"])

    # Embedded descriptions count
    r = run_query(product_catalog,
        "SELECT VALUE COUNT(1) FROM c JOIN d IN c.descriptions WHERE c.type = 'model'")
    check("Embedded descriptions count", r[0], 762)

    # Spot-check: product with category snapshot
    r = run_query(product_catalog,
        "SELECT TOP 1 c.productId, c.name, c.category.name AS categoryName "
        "FROM c WHERE c.type = 'product' AND c.category != null")
    if r:
        print(f"  [INFO] Product {r[0]['productId']} '{r[0]['name']}' -> category '{r[0]['categoryName']}'")

    # Verify all documents have _schemaVersion = 1
    print("\n--- Cross-cutting checks ---")
    for cname, container in [("customer-orders", customer_orders), ("product-catalog", product_catalog)]:
        r = run_query(container,
            "SELECT VALUE COUNT(1) FROM c WHERE NOT IS_DEFINED(c._schemaVersion) OR c._schemaVersion != 1")
        check(f"{cname}: docs missing _schemaVersion=1", r[0], 0)

    # Summary
    print("\n" + "=" * 60)
    if all_pass:
        print("ALL VALIDATIONS PASSED")
    else:
        print("SOME VALIDATIONS FAILED — review output above")
    print("=" * 60)

    return 0 if all_pass else 1


if __name__ == "__main__":
    sys.exit(main())
