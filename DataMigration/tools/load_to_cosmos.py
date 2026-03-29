"""
Load JSON documents into Azure Cosmos DB containers.

Reads generated JSON files from DataMigration/data/ and bulk-loads them
into the deployed Cosmos DB account using the Azure Cosmos DB Python SDK.

Requirements:
  pip install azure-cosmos azure-identity

Environment:
  Uses DefaultAzureCredential (Entra ID) — no access keys needed.
"""

import json
import os
import sys
import time
from pathlib import Path

from azure.cosmos import CosmosClient, PartitionKey, exceptions
from azure.identity import DefaultAzureCredential

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
COSMOS_ENDPOINT = "https://ssm-cosmos-adwlt02.documents.azure.com:443/"
DATABASE_NAME = "adwkslt"

# Container configs: name -> partition key path
CONTAINERS = {
    "customer-orders": "/customerId",
    "product-catalog": "/partitionKey",
}

# Data files mapped to containers
DATA_FILES = {
    "customer-orders": [
        "customers.json",
        "sales-orders.json",
    ],
    "product-catalog": [
        "categories.json",
        "models.json",
        "products.json",
    ],
}

SCRIPT_DIR = Path(__file__).resolve().parent
DATA_DIR = SCRIPT_DIR.parent / "data"


def get_partition_key_value(doc: dict, container_name: str):
    """Extract the partition key value from a document based on container."""
    if container_name == "customer-orders":
        return doc["customerId"]
    elif container_name == "product-catalog":
        return doc["partitionKey"]
    else:
        raise ValueError(f"Unknown container: {container_name}")


def load_container(container, container_name: str, data_file: str) -> dict:
    """Load all documents from a JSON file into the container."""
    filepath = DATA_DIR / container_name / data_file
    if not filepath.exists():
        print(f"  WARNING: {filepath} not found, skipping")
        return {"loaded": 0, "failed": 0, "errors": []}

    with open(filepath, "r", encoding="utf-8") as f:
        documents = json.load(f)

    loaded = 0
    failed = 0
    errors = []
    total = len(documents)

    print(f"  Loading {data_file} ({total} documents)...")

    for i, doc in enumerate(documents):
        try:
            pk_value = get_partition_key_value(doc, container_name)
            container.upsert_item(doc)
            loaded += 1
        except exceptions.CosmosHttpResponseError as e:
            failed += 1
            errors.append(f"  Doc {doc.get('id', '?')}: {e.message}")
            if failed <= 3:
                print(f"    ERROR: {doc.get('id', '?')}: {e.message}")
            elif failed == 4:
                print(f"    ... suppressing further error details")

        # Progress indicator every 100 docs
        if (i + 1) % 100 == 0 or (i + 1) == total:
            pct = (i + 1) / total * 100
            print(f"    [{i+1}/{total}] {pct:.0f}% — loaded: {loaded}, failed: {failed}")

    return {"loaded": loaded, "failed": failed, "errors": errors}


def main():
    print("=" * 60)
    print("Cosmos DB Document Loader")
    print(f"Endpoint: {COSMOS_ENDPOINT}")
    print(f"Database: {DATABASE_NAME}")
    print("=" * 60)

    # Authenticate with Entra ID
    print("\n[1/3] Authenticating with DefaultAzureCredential...")
    credential = DefaultAzureCredential()
    client = CosmosClient(COSMOS_ENDPOINT, credential=credential)
    print("  Authenticated successfully")

    # Get database
    print("\n[2/3] Connecting to database...")
    database = client.get_database_client(DATABASE_NAME)
    print(f"  Connected to database: {DATABASE_NAME}")

    # Load data
    print("\n[3/3] Loading documents into containers...")
    summary = {}
    start_time = time.time()

    for container_name, data_files in DATA_FILES.items():
        print(f"\n--- Container: {container_name} ---")
        container = database.get_container_client(container_name)

        container_total = {"loaded": 0, "failed": 0}
        for data_file in data_files:
            result = load_container(container, container_name, data_file)
            container_total["loaded"] += result["loaded"]
            container_total["failed"] += result["failed"]

        summary[container_name] = container_total

    elapsed = time.time() - start_time

    # Summary
    print("\n" + "=" * 60)
    print("Load Summary")
    print("=" * 60)
    grand_loaded = 0
    grand_failed = 0
    for container_name, stats in summary.items():
        print(f"  {container_name}: {stats['loaded']} loaded, {stats['failed']} failed")
        grand_loaded += stats["loaded"]
        grand_failed += stats["failed"]

    print(f"\n  TOTAL: {grand_loaded} loaded, {grand_failed} failed")
    print(f"  Time: {elapsed:.1f}s")
    print("=" * 60)

    return 0 if grand_failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
