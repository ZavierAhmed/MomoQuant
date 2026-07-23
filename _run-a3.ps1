$ErrorActionPreference = 'Stop'
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$base = 'https://localhost:7295'
$outDir = 'C:\Users\zasah\Documents\MOMO Quant'

$login = Invoke-RestMethod -Uri "$base/api/v1/auth/login" -Method Post -Body '{"email":"admin@momoquant.local","password":"Admin123!"}' -ContentType 'application/json'
$token = $login.data.accessToken
$h = @{ Authorization = "Bearer $token" }

# Observation settings from prior A2 (Run 28 config)
$obs = @{
  confidenceModel = 'StrategySetupQuality/v1'
  useSystemDefaultConfidenceThreshold = $false
  customConfidenceThreshold = 65
  useSystemDefaultRiskSettings = $false
  riskApprovalThreshold = 50
  riskPerTradePercent = 0.5
  preferredLeverage = 10
  maximumLeverage = 10
  maxMarginUsagePerSymbolPercent = 25
  maxTotalMarginUsagePercent = 50
  maxConcurrentRiskPercent = 2
  maxOpenPositions = 2
  maxDailyLossPercent = 2
  maxDrawdownPercent = 5
  minimumRewardRisk = 1.2
  exposureSemanticsVersion = 4
  entryOrderType = 'Taker'
  exitOrderType = 'Taker'
}

$qual = @{
  profileVersion = 'StandardHoldoutQualification/v1'
  primaryQualificationLayer = 'RawStrategy'
  minimumTrainingClosedTrades = 30
  minimumValidationClosedTrades = 15
  minimumTrainingProfitFactor = 1.10
  minimumValidationProfitFactor = 1.05
  minimumTrainingNetExpectancyR = 0
  minimumValidationNetExpectancyR = 0
  maximumTrainingDrawdownPercent = 25
  maximumValidationDrawdownPercent = 25
  minimumOpportunityRetentionPercent = 40
  maximumAllowedExpectancyDegradation = 0.50
  maximumSingleTradePnlContributionPercent = 40
  requirePositiveValidationNetPnl = $true
  requirePositiveValidationNetExpectancy = $true
  requireParameterStability = $true
}

$createBody = @{
  name = 'Manual Verification A3 - Run 28 Exclusivity'
  description = 'ValidateExistingFrozenConfiguration from Strategy Lab Run 28 with HoldoutExclusivity/v1 and ValidationMetrics/v1.2'
  experimentType = 'ValidateExistingFrozenConfiguration'
  strategyCode = 'PRICE_STRUCTURE_BREAKOUT_RETEST'
  sourceStrategyLabRunId = 28
  exchangeId = 3
  symbolId = 371
  timeframe = '15m'
  requestedStartUtc = '2026-07-01T00:00:00Z'
  requestedEndUtc = '2026-07-14T23:59:59Z'
  splitRatio = 0.70
  requiredWarmupCandles = 600
  maximumTrials = 1
  deterministicSeed = 42
  initialBalance = 100
  makerFeeRate = 0.0002
  takerFeeRate = 0.0004
  slippagePercent = 0
  autoImportMissingCandles = $true
  observationSettings = $obs
  qualificationProfile = $qual
} | ConvertTo-Json -Depth 8

Write-Output '=== CREATE A3 ==='
$create = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments" -Method Post -Headers $h -Body $createBody -ContentType 'application/json'
$create | ConvertTo-Json -Depth 6 | Set-Content "$outDir\a3-create.json" -Encoding utf8
if (-not $create.success) { throw "Create failed: $($create.errorMessage)" }
$expId = $create.data.id
Write-Output "A3 experiment id=$expId status=$($create.data.status) metricsVer=$($create.data.validationMetricsVersion) warmup=$($create.data.requiredWarmupCandles)"
$expId | Set-Content "$outDir\a3-exp-id.txt" -Encoding utf8

Write-Output '=== PREPARE ==='
$prep = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId/prepare-data" -Method Post -Headers $h -TimeoutSec 600
$prep | ConvertTo-Json -Depth 6 | Set-Content "$outDir\a3-prepare.json" -Encoding utf8
Write-Output "prep success=$($prep.success) status=$($prep.data.status) candles=$($prep.data.totalEligibleCandleCount) fp=$($prep.data.candleDataFingerprint) err=$($prep.errorMessage)"
if (-not $prep.success) { throw "Prepare failed" }

Write-Output '=== TRAIN ==='
$train = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId/run-training" -Method Post -Headers $h -TimeoutSec 1800
$train | ConvertTo-Json -Depth 6 | Set-Content "$outDir\a3-training.json" -Encoding utf8
Write-Output "train success=$($train.success) status=$($train.data.status) err=$($train.errorMessage)"
if (-not $train.success) { throw "Training failed" }

Write-Output '=== FREEZE ==='
$freeze = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId/freeze" -Method Post -Headers $h -TimeoutSec 300
$freeze | ConvertTo-Json -Depth 6 | Set-Content "$outDir\a3-freeze.json" -Encoding utf8
Write-Output "freeze success=$($freeze.success) status=$($freeze.data.status) frozenFp=$($freeze.data.frozenParameterFingerprint) err=$($freeze.errorMessage)"
if (-not $freeze.success) { throw "Freeze failed" }

Write-Output '=== VALIDATE ==='
$val = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId/run-validation" -Method Post -Headers $h -TimeoutSec 1800
$val | ConvertTo-Json -Depth 8 | Set-Content "$outDir\a3-validation.json" -Encoding utf8
Write-Output "validate success=$($val.success) status=$($val.data.status) verdict=$($val.data.strategyRobustnessDecision) err=$($val.errorMessage)"
if (-not $val.success) { throw "Validation failed" }

Write-Output '=== DETAIL ==='
$detail = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId" -Headers $h
$detail | ConvertTo-Json -Depth 12 | Set-Content "$outDir\a3-detail.json" -Encoding utf8

Write-Output '=== EXCLUSIVITY ==='
try {
  $excl = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId/exclusivity" -Headers $h
  $excl | ConvertTo-Json -Depth 12 | Set-Content "$outDir\a3-exclusivity.json" -Encoding utf8
} catch { "exclusivity endpoint err: $($_.Exception.Message)" | Set-Content "$outDir\a3-exclusivity.json" -Encoding utf8 }

Write-Output '=== EXPORTS ==='
foreach ($fmt in @('Json','Csv','Pdf')) {
  $body = @{ scope = 'ValidationExperiment'; sourceId = "$expId"; format = $fmt; detailLevel = 'Full' } | ConvertTo-Json
  try {
    $job = Invoke-RestMethod -Uri "$base/api/v1/exports" -Method Post -Headers $h -Body $body -ContentType 'application/json'
    "export $fmt success=$($job.success) id=$($job.data.exportId) status=$($job.data.status) err=$($job.errorMessage)"
    $jid = $job.data.exportId
    if ($jid) {
      Start-Sleep -Seconds 2
      $ext = switch ($fmt) { 'Json' { 'json' } 'Csv' { 'zip' } 'Pdf' { 'pdf' } }
      $outFile = "$outDir\a3-export-$fmt-$jid.$ext"
      Invoke-WebRequest -Uri "$base/api/v1/exports/$jid/download" -Headers $h -OutFile $outFile
      "downloaded $outFile size=$((Get-Item $outFile).Length)"
    }
  } catch { "export $fmt err: $($_.Exception.Message)" }
}

# Refresh detail after exports (verification status)
$detail2 = Invoke-RestMethod -Uri "$base/api/v1/validation-lab/experiments/$expId" -Headers $h
$detail2 | ConvertTo-Json -Depth 12 | Set-Content "$outDir\a3-detail-after-export.json" -Encoding utf8

$d = $detail2.data
Write-Output "=== A3 SUMMARY ==="
Write-Output "id=$($d.id) status=$($d.status) metrics=$($d.validationMetricsVersion) exclusivity=$($d.holdoutExclusivityPolicyVersion)"
Write-Output "verdict=$($d.strategyRobustnessDecision) primary=$($d.primaryFailureReason)"
Write-Output "overlapCount=$($d.crossSegmentOverlapCount) consistency=$($d.metricConsistencyStatus) exportVerif=$($d.exportVerificationStatus) readiness=$($d.validationLaboratoryReadinessStatus)"
$raw = $d.segmentResults | Where-Object { $_.layerType -eq 'RawStrategy' }
foreach ($s in $raw) {
  Write-Output "seg=$($s.segmentType) persisted=$($s.persistedCandidateRowCount) included=$($s.metricIncludedCandidateCount) excluded=$($s.metricExcludedCandidateCount) overlap=$($s.crossSegmentOverlapCount) closed=$($s.closedTradeCount) gExp=$($s.grossExpectancyR) nExp=$($s.netExpectancyR) gPF=$($s.grossProfitFactor) nPF=$($s.netProfitFactor) ver=$($s.resultCalculationVersion)"
}
Write-Output "A3_DONE id=$expId"
