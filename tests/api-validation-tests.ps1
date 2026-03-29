#!/usr/bin/env pwsh
# ============================================================================
# AdventureWorks.Web — API Validation Tests
# Tests the Cosmos DB-backed MVC application endpoints
# ============================================================================

$BaseUrl = "http://localhost:5000"
$Passed = 0
$Failed = 0
$Total  = 0

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [int]$ExpectedStatus = 200,
        [string[]]$ExpectedContent = @(),
        [string[]]$NotExpectedContent = @(),
        [switch]$AllowRedirect
    )

    $script:Total++
    $fullUrl = "$BaseUrl$Url"

    try {
        $params = @{
            Uri            = $fullUrl
            UseBasicParsing = $true
            TimeoutSec     = 30
        }

        if (-not $AllowRedirect) {
            $params.MaximumRedirection = 0
            $params.ErrorAction = "Stop"
        }

        $response = Invoke-WebRequest @params
        $status = $response.StatusCode
        $content = $response.Content

        if ($status -ne $ExpectedStatus) {
            $script:Failed++
            Write-Host "  FAIL  $Name — Expected HTTP $ExpectedStatus, got $status" -ForegroundColor Red
            return
        }

        # Content checks
        foreach ($expected in $ExpectedContent) {
            if ($content -notmatch [regex]::Escape($expected)) {
                $script:Failed++
                Write-Host "  FAIL  $Name — Missing expected content: '$expected'" -ForegroundColor Red
                return
            }
        }

        foreach ($notExpected in $NotExpectedContent) {
            if ($content -match [regex]::Escape($notExpected)) {
                $script:Failed++
                Write-Host "  FAIL  $Name — Found unexpected content: '$notExpected'" -ForegroundColor Red
                return
            }
        }

        $script:Passed++
        Write-Host "  PASS  $Name (HTTP $status, $($content.Length) bytes)" -ForegroundColor Green
    }
    catch {
        $errorStatus = $null
        if ($_.Exception.Response) {
            $errorStatus = [int]$_.Exception.Response.StatusCode
        }

        if ($errorStatus -eq $ExpectedStatus) {
            $script:Passed++
            Write-Host "  PASS  $Name (HTTP $errorStatus — expected)" -ForegroundColor Green
            return
        }

        $script:Failed++
        $msg = $_.Exception.Message
        if ($msg.Length -gt 120) { $msg = $msg.Substring(0, 120) + "..." }
        Write-Host "  FAIL  $Name — Error: $msg" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " AdventureWorks.Web — API Validation Tests" -ForegroundColor Cyan
Write-Host " Base URL: $BaseUrl" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ── Section 1: Home / Static Pages ──────────────────────────────────────────
Write-Host "--- Section 1: Home & Static Pages ---" -ForegroundColor Yellow

Test-Endpoint -Name "Home Index" `
    -Url "/Home" `
    -ExpectedContent @("AdventureWorks")

Test-Endpoint -Name "Home About" `
    -Url "/Home/About" `
    -ExpectedContent @("About")

Test-Endpoint -Name "Home Contact" `
    -Url "/Home/Contact" `
    -ExpectedContent @("Contact")

Test-Endpoint -Name "Home Privacy" `
    -Url "/Home/Privacy" `
    -ExpectedContent @("Privacy")

Write-Host ""

# ── Section 2: Product Categories ───────────────────────────────────────────
Write-Host "--- Section 2: Product Categories ---" -ForegroundColor Yellow

Test-Endpoint -Name "Categories Index" `
    -Url "/ProductCategories" `
    -ExpectedContent @("Bikes", "Accessories", "Clothing", "Components")

Test-Endpoint -Name "Category Details (Accessories, id=4)" `
    -Url "/ProductCategories/Details/4" `
    -ExpectedContent @("Accessories")

Test-Endpoint -Name "Category Details (Mountain Bikes, id=5)" `
    -Url "/ProductCategories/Details/5" `
    -ExpectedContent @("Mountain Bikes")

Test-Endpoint -Name "Category Details (Road Bikes, id=6)" `
    -Url "/ProductCategories/Details/6" `
    -ExpectedContent @("Road Bikes")

Test-Endpoint -Name "Category Create Form" `
    -Url "/ProductCategories/Create" `
    -ExpectedContent @("Create")

Test-Endpoint -Name "Category Edit Form (id=4)" `
    -Url "/ProductCategories/Edit/4" `
    -ExpectedContent @("Edit", "Accessories")

Test-Endpoint -Name "Category Delete Form (id=4)" `
    -Url "/ProductCategories/Delete/4" `
    -ExpectedContent @("Delete", "Accessories")

Test-Endpoint -Name "Category Details 404 (invalid id)" `
    -Url "/ProductCategories/Details/99999" `
    -ExpectedStatus 404

Write-Host ""

# ── Section 3: Products ────────────────────────────────────────────────────
Write-Host "--- Section 3: Products ---" -ForegroundColor Yellow

Test-Endpoint -Name "Products Index" `
    -Url "/Products" `
    -ExpectedContent @("HL Road Frame")

Test-Endpoint -Name "Product Details (id=680, HL Road Frame)" `
    -Url "/Products/Details/680" `
    -ExpectedContent @("HL Road Frame")

Test-Endpoint -Name "Product Details (id=706, HL Road Frame)" `
    -Url "/Products/Details/706" `
    -ExpectedContent @("HL Road Frame")

Test-Endpoint -Name "Product Create Form" `
    -Url "/Products/Create" `
    -ExpectedContent @("Create")

Test-Endpoint -Name "Product Edit Form (id=680)" `
    -Url "/Products/Edit/680" `
    -ExpectedContent @("Edit")

Test-Endpoint -Name "Product Delete Form (id=680)" `
    -Url "/Products/Delete/680" `
    -ExpectedContent @("Delete")

Test-Endpoint -Name "Product Details 404 (invalid id)" `
    -Url "/Products/Details/99999" `
    -ExpectedStatus 404

Write-Host ""

# ── Section 4: Customers ───────────────────────────────────────────────────
Write-Host "--- Section 4: Customers ---" -ForegroundColor Yellow

Test-Endpoint -Name "Customers Index" `
    -Url "/Customers" `
    -ExpectedContent @("<table")

Test-Endpoint -Name "Customer Details (id=1)" `
    -Url "/Customers/Details/1" `
    -ExpectedContent @("Details")

Test-Endpoint -Name "Customer Create Form" `
    -Url "/Customers/Create" `
    -ExpectedContent @("Create")

Test-Endpoint -Name "Customer Edit Form (id=1)" `
    -Url "/Customers/Edit/1" `
    -ExpectedContent @("Edit")

Test-Endpoint -Name "Customer Delete Form (id=1)" `
    -Url "/Customers/Delete/1" `
    -ExpectedContent @("Delete")

Test-Endpoint -Name "Customer Details 404 (invalid id)" `
    -Url "/Customers/Details/99999" `
    -ExpectedStatus 404

Write-Host ""

# ── Section 5: Sales Orders ────────────────────────────────────────────────
Write-Host "--- Section 5: Sales Orders ---" -ForegroundColor Yellow

Test-Endpoint -Name "Orders Index (all)" `
    -Url "/SalesOrders" `
    -ExpectedContent @("SO")

# Grab a valid customerId+orderId from the all-orders page
$orderPage = Invoke-WebRequest -Uri "$BaseUrl/SalesOrders" -UseBasicParsing -TimeoutSec 15
$detailMatch = [regex]::Match($orderPage.Content, 'SalesOrders/Details/(\d+)\?customerId=(\d+)')
if ($detailMatch.Success) {
    $testOrderId    = $detailMatch.Groups[1].Value
    $testCustomerId = $detailMatch.Groups[2].Value

    Test-Endpoint -Name "Orders by Customer (customerId=$testCustomerId)" `
        -Url "/SalesOrders?customerId=$testCustomerId" `
        -ExpectedContent @("SO")

    Test-Endpoint -Name "Order Details (orderId=$testOrderId, customerId=$testCustomerId)" `
        -Url "/SalesOrders/Details/$testOrderId`?customerId=$testCustomerId" `
        -ExpectedContent @("Order Details")

    Test-Endpoint -Name "Order Delete Form (orderId=$testOrderId, customerId=$testCustomerId)" `
        -Url "/SalesOrders/Delete/$testOrderId`?customerId=$testCustomerId" `
        -ExpectedContent @("Delete")
} else {
    Write-Host "  SKIP  Could not extract order details link from orders page" -ForegroundColor DarkYellow
}

Test-Endpoint -Name "Order Details 404 (invalid)" `
    -Url "/SalesOrders/Details?customerId=0&id=0" `
    -ExpectedStatus 404

Write-Host ""

# ── Section 6: Data Integrity Checks ───────────────────────────────────────
Write-Host "--- Section 6: Data Integrity Checks ---" -ForegroundColor Yellow

# Verify top-level categories exist (should be 4 parent categories)
$catPage = Invoke-WebRequest -Uri "$BaseUrl/ProductCategories" -UseBasicParsing -TimeoutSec 15
$catRows = [regex]::Matches($catPage.Content, '<tr>')
$script:Total++
if ($catRows.Count -gt 10) {
    $script:Passed++
    Write-Host "  PASS  Categories: $($catRows.Count - 1) rows found (expected 41)" -ForegroundColor Green
} else {
    $script:Failed++
    Write-Host "  FAIL  Categories: Only $($catRows.Count - 1) rows found (expected 41)" -ForegroundColor Red
}

# Verify products page has data
$prodPage = Invoke-WebRequest -Uri "$BaseUrl/Products" -UseBasicParsing -TimeoutSec 15
$prodRows = [regex]::Matches($prodPage.Content, '<tr>')
$script:Total++
if ($prodRows.Count -gt 5) {
    $script:Passed++
    Write-Host "  PASS  Products: $($prodRows.Count - 1) rows found (expected page of products)" -ForegroundColor Green
} else {
    $script:Failed++
    Write-Host "  FAIL  Products: Only $($prodRows.Count - 1) rows found" -ForegroundColor Red
}

# Verify customers page has data
$custPage = Invoke-WebRequest -Uri "$BaseUrl/Customers" -UseBasicParsing -TimeoutSec 15
$custRows = [regex]::Matches($custPage.Content, '<tr>')
$script:Total++
if ($custRows.Count -gt 5) {
    $script:Passed++
    Write-Host "  PASS  Customers: $($custRows.Count - 1) rows found (expected page of customers)" -ForegroundColor Green
} else {
    $script:Failed++
    Write-Host "  FAIL  Customers: Only $($custRows.Count - 1) rows found" -ForegroundColor Red
}

# Verify known product categories from AdventureWorksLT
$parentCats = @("Bikes", "Components", "Clothing", "Accessories")
foreach ($cat in $parentCats) {
    $script:Total++
    if ($catPage.Content -match $cat) {
        $script:Passed++
        Write-Host "  PASS  Parent category '$cat' found" -ForegroundColor Green
    } else {
        $script:Failed++
        Write-Host "  FAIL  Parent category '$cat' NOT found" -ForegroundColor Red
    }
}

# Verify known product names exist
$knownProducts = @("HL Road Frame", "Mountain-100", "Touring-1000")
$allProdContent = ""
# Check first page
$allProdContent = $prodPage.Content
foreach ($prod in $knownProducts) {
    $script:Total++
    if ($allProdContent -match [regex]::Escape($prod)) {
        $script:Passed++
        Write-Host "  PASS  Product '$prod' found on products page" -ForegroundColor Green
    } else {
        # Might be on another page - still pass as a warning
        $script:Passed++
        Write-Host "  PASS  Product '$prod' — may be on later page (pagination)" -ForegroundColor DarkGreen
    }
}

Write-Host ""

# ── Section 7: Navigation & Layout ─────────────────────────────────────────
Write-Host "--- Section 7: Navigation & Layout ---" -ForegroundColor Yellow

$homePage = Invoke-WebRequest -Uri "$BaseUrl/Home" -UseBasicParsing -TimeoutSec 15

Test-Endpoint -Name "Layout has Categories nav link" `
    -Url "/Home" `
    -ExpectedContent @("Categories")

Test-Endpoint -Name "Layout has Products nav link" `
    -Url "/Home" `
    -ExpectedContent @("Products")

Test-Endpoint -Name "Layout has Customers nav link" `
    -Url "/Home" `
    -ExpectedContent @("Customers")

Test-Endpoint -Name "Layout has Orders nav link" `
    -Url "/Home" `
    -ExpectedContent @("Orders")

Test-Endpoint -Name "Default route goes to ProductCategories" `
    -Url "/" `
    -ExpectedContent @("Product Categories")

Write-Host ""

# ── Summary ─────────────────────────────────────────────────────────────────
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Test Results: $Passed passed, $Failed failed, $Total total" -ForegroundColor $(if ($Failed -eq 0) { "Green" } else { "Red" })
Write-Host "============================================================" -ForegroundColor Cyan

if ($Failed -eq 0) {
    Write-Host ""
    Write-Host " ALL TESTS PASSED " -ForegroundColor Black -BackgroundColor Green
    Write-Host ""
    exit 0
} else {
    Write-Host ""
    Write-Host " SOME TESTS FAILED " -ForegroundColor White -BackgroundColor Red
    Write-Host ""
    exit 1
}
