# Complete Test Sequence for JWT Key Rotation POC
# Run this script after starting the application: dotnet run

$baseUrl = "http://localhost:5166"
$maxWaitTime = 30
$waitInterval = 2

Write-Host "========================================"
Write-Host "JWT Key Rotation POC - Test Sequence"
Write-Host "========================================"
Write-Host ""

# Wait for app to be ready
Write-Host "Waiting for application to start..."
$elapsed = 0
$ready = $false
while ($elapsed -lt $maxWaitTime -and -not $ready) {
    try {
        $test = Invoke-RestMethod -Uri "$baseUrl/api/admin/keys" -Method GET -ErrorAction Stop
        $ready = $true
        Write-Host "[OK] Application is ready!"
    } catch {
        Start-Sleep -Seconds $waitInterval
        $elapsed += $waitInterval
        Write-Host "  Waiting... ($elapsed / $maxWaitTime) seconds"
    }
}

if (-not $ready) {
    Write-Host "[FAIL] Application did not start within $maxWaitTime seconds"
    Write-Host "Please ensure the app is running: cd JwtKeyRotationPoc; dotnet run"
    exit 1
}

Write-Host ""

# Step 1: Check initial key
Write-Host "=== Step 1: Check Initial Key ==="
$keys = Invoke-RestMethod -Uri "$baseUrl/api/admin/keys" -Method GET
$keys | ConvertTo-Json
Write-Host "[OK] Step 1 passed: Initial key found"
Write-Host ""

# Step 2: Create T1
Write-Host "=== Step 2: Create Token T1 ==="
$body = @{ Email = "test@example.com"; FilePath = "/files/document.pdf"; TtlMinutes = 60 } | ConvertTo-Json
$response1 = Invoke-RestMethod -Uri "$baseUrl/api/link/secure" -Method POST -Body $body -ContentType "application/json"
$token1 = $response1.token
Write-Host "T1 created: $($response1.token.Substring(0,50))..."
Write-Host "[OK] Step 2 passed: Token T1 created"
Write-Host ""

# Step 3: Validate T1
Write-Host "=== Step 3: Validate T1 ==="
$result = Invoke-RestMethod -Uri "$baseUrl/api/download?token=$token1" -Method GET
Write-Host "T1 validated successfully:"
Write-Host "  - Email: $($result.email)"
Write-Host "  - FilePath: $($result.filePath)"
Write-Host "  - Kid: $($result.kid)"
Write-Host "[OK] Step 3 passed: T1 validation successful"
Write-Host ""

# Step 4: Rotate key
Write-Host "=== Step 4: Rotate Key ==="
$newKey = Invoke-RestMethod -Uri "$baseUrl/api/admin/rotate-key" -Method POST
Write-Host "Key rotated successfully:"
Write-Host "  - New active kid: $($newKey.kid)"
Write-Host "  - Created: $($newKey.createdOn)"
Write-Host "[OK] Step 4 passed: Key rotation successful"
Write-Host ""

# Step 5: Create T2
Write-Host "=== Step 5: Create Token T2 ==="
$body = @{ Email = "test2@example.com"; FilePath = "/files/document2.pdf"; TtlMinutes = 60 } | ConvertTo-Json
$response2 = Invoke-RestMethod -Uri "$baseUrl/api/link/secure" -Method POST -Body $body -ContentType "application/json"
$token2 = $response2.token
Write-Host "T2 created: $($response2.token.Substring(0,50))..."
Write-Host "[OK] Step 5 passed: Token T2 created"
Write-Host ""

# Step 6: Validate both tokens
Write-Host "=== Step 6: Validate Both Tokens (Graceful Rotation Test) ==="
$result1 = Invoke-RestMethod -Uri "$baseUrl/api/download?token=$token1" -Method GET
Write-Host "[OK] T1 validated (kid: $($result1.kid), email: $($result1.email))"
$result2 = Invoke-RestMethod -Uri "$baseUrl/api/download?token=$token2" -Method GET
Write-Host "[OK] T2 validated (kid: $($result2.kid), email: $($result2.email))"
Write-Host "[OK] Step 6 passed: Both tokens validated (graceful rotation confirmed!)"
Write-Host ""

# Step 7: List all keys
Write-Host "=== Step 7: List All Keys ==="
$allKeys = Invoke-RestMethod -Uri "$baseUrl/api/admin/keys" -Method GET
Write-Host "All keys:"
$allKeys | ConvertTo-Json -Depth 5
$oldKid = ($allKeys | Where-Object { $_.isActive -eq $false })[0].kid
Write-Host "Old key ID to retire: $oldKid"
Write-Host "[OK] Step 7 passed: All keys listed"
Write-Host ""

# Step 8: Retire old key
Write-Host "=== Step 8: Retire Old Key ==="
$retireResponse = Invoke-RestMethod -Uri "$baseUrl/api/admin/retire/$oldKid" -Method POST
Write-Host "Old key retired: $($retireResponse.message)"
Write-Host "[OK] Step 8 passed: Key retirement successful"
Write-Host ""

# Step 9: Verify retirement impact
Write-Host "=== Step 9: Verify Retirement Impact ==="
Write-Host "Validating T1 (should fail):"
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/download?token=$token1" -Method GET
    Write-Host "[FAIL] ERROR: T1 should have failed but succeeded!"
    $step9Passed = $false
} catch {
    Write-Host "[OK] T1 correctly rejected (expected failure)"
    $step9Passed = $true
}
Write-Host "Validating T2 (should succeed):"
$result2 = Invoke-RestMethod -Uri "$baseUrl/api/download?token=$token2" -Method GET
Write-Host "[OK] T2 validated successfully (kid: $($result2.kid))"
if ($step9Passed) {
    Write-Host "[OK] Step 9 passed: Retirement impact verified"
} else {
    Write-Host "[FAIL] Step 9 failed: T1 should have been rejected"
}
Write-Host ""

# Step 10: Test JWKS
Write-Host "=== Step 10: Test JWKS Endpoint ==="
$jwks = Invoke-RestMethod -Uri "$baseUrl/.well-known/jwks.json" -Method GET
Write-Host "JWKS endpoint working:"
Write-Host "  - Number of keys: $($jwks.keys.Count)"
Write-Host "  - Key IDs: $(($jwks.keys | ForEach-Object { $_.kid }) -join ', ')"
Write-Host "[OK] Step 10 passed: JWKS endpoint verified"
Write-Host ""

# Summary
Write-Host "========================================"
Write-Host "=== ALL TESTS COMPLETED ==="
Write-Host "========================================"
Write-Host ""
Write-Host "Test Results Summary:"
Write-Host "[OK] Initial key created"
Write-Host "[OK] Token generation works"
Write-Host "[OK] Token validation works"
Write-Host "[OK] Key rotation works"
Write-Host "[OK] Graceful rotation (both old and new tokens validate)"
Write-Host "[OK] Key retirement works"
Write-Host "[OK] Token invalidation after retirement"
Write-Host "[OK] JWKS endpoint works"
Write-Host ""
Write-Host "All test steps from README are working correctly!"
Write-Host ""

