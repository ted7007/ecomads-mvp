# Recommendation business rules

Этот документ фиксирует правила для алгоритмического слоя рекомендаций. Использовать как источник истины при реализации `RecommendationEngine`.

## Numeric rules

All divisions must be safe.

```text
DRR = Spend / Revenue * 100
CTR = Clicks / Impressions * 100
CPC = Spend / Clicks
CR = Orders / Clicks * 100
CPO = Spend / Orders
AverageOrderValue = Revenue / Orders
AvgDailyOrders = Orders / PeriodDays
```

Null handling:

- If `Revenue = 0`, `Drr = null`.
- If `Impressions = 0`, `Ctr = null`.
- If `Clicks = 0`, `Cpc = null` and `Cr = null`.
- If `Orders = 0`, `Cpo = null` and `AverageOrderValue = null`.
- If `PeriodDays <= 0`, use `1`.

For imported values, prefer recalculated metrics over stored spreadsheet metrics when all source fields are available.

## Confidence

Options:

```text
MinClicksForConclusion = 30
MinSpendForConclusion = 500
MinOrdersForPositiveConclusion = 3
MinViewsForCtrConclusion = 1000
```

Levels:

```text
High:
  Clicks >= 100
  or Spend >= 3000
  or Orders >= 10

Medium:
  Clicks >= 30
  or Spend >= 500
  or Orders >= 3

Low:
  everything below Medium
```

Scores:

```text
Low = 0.4
Medium = 0.7
High = 1.0
```

Low confidence does not always remove an insight, but it lowers priority and blocks aggressive actions.

## Required MVP insight types

```text
BadSpendWithoutOrders
BadDrr
ScaleCandidate
WatchCandidate
LowData
StockRisk
SeasonRisk
```

`StockRisk` and `SeasonRisk` can be generated only when stock or season context is available. If current API does not provide that context, the MVP implementation should keep the types and scoring code ready but skip these insights.

## Keyword rules

### LowData

Condition:

```text
Clicks < MinClicksForConclusion
and Spend < MinSpendForConclusion
and Orders < MinOrdersForPositiveConclusion
```

Allowed actions:

```text
CollectMoreData
Watch
```

Forbidden actions:

```text
MinusKeyword
Scale
AggressiveBidChange
ImmediateMinusKeyword
```

Reason codes:

```text
low_confidence
not_enough_clicks
not_enough_spend
```

### BadSpendWithoutOrders

Condition:

```text
Spend >= MinSpendForConclusion
and Orders = 0
and Clicks >= MinClicksForConclusion
```

Allowed actions:

```text
DecreaseBid
ConsiderMinusKeyword
MoveToWatchlist
```

Forbidden actions:

```text
IncreaseBid
Scale
```

Reason codes:

```text
significant_spend_without_orders
```

### BadDrr

Condition:

```text
Orders > 0
and Drr is not null
and Drr > TargetDrr
and Spend >= MinSpendForConclusion
and ConfidenceLevel != Low
```

Severity:

```text
Drr <= TargetDrr * 1.2 -> mild
Drr <= TargetDrr * 1.5 -> medium
Drr > TargetDrr * 1.5 -> strong
```

Allowed actions:

```text
DecreaseBid
Watch
Optimize
```

Additional allowed action for strong deviation and low orders:

```text
ConsiderMinusKeyword
```

Forbidden actions:

```text
IncreaseBid
Scale
```

Reason codes:

```text
drr_above_target
```

### ScaleCandidate

Condition:

```text
Orders >= MinOrdersForPositiveConclusion
and Drr is not null
and Drr <= TargetDrr
and ConfidenceLevel != Low
```

Allowed actions:

```text
IncreaseBidGradually
Scale
FindSimilarKeywords
Maintain
```

Forbidden actions:

```text
MinusKeyword
Disable
ImmediateMinusKeyword
ImmediateDisable
```

Reason codes:

```text
drr_below_target
has_stable_orders
growth_candidate
keyword_converts
```

### WatchCandidate

Condition:

```text
ConfidenceLevel = Low
or (
  Orders > 0
  and Drr is not null
  and Drr > TargetDrr
  and Drr <= TargetDrr * 1.2
)
```

Allowed actions:

```text
Watch
CollectMoreData
DecreaseBidCarefully
```

Forbidden actions:

```text
AggressiveScale
ImmediateDisable
```

Reason codes:

```text
watch_candidate
low_confidence
small_drr_deviation
```

## Stock and season rules

### SeasonScore

```text
DaysUntilDemandDrop > 60 -> 1.0
31-60 -> 1.2
15-30 -> 1.5
7-14 -> 2.0
1-6 -> 2.5
<= 0 -> 3.0
unknown -> 1.0
```

### UrgencyScore for season

```text
DaysUntilDemandDrop > 60 -> 1.0
31-60 -> 1.1
15-30 -> 1.3
7-14 -> 1.6
1-6 -> 2.0
<= 0 -> 2.2
unknown -> 1.0
```

### StockRiskScore

Without stock:

```text
1.0
```

With stock and deadline:

```text
SalesPaceCoverage >= 1.0 -> 1.0
0.7-1.0 -> 1.3
0.4-0.7 -> 1.7
< 0.4 -> 2.2
AvgDailyOrders = 0 -> 2.5
```

### StockRisk

Condition:

```text
Stock > 0
and DaysUntilDemandDrop is known
and (
  DaysToSellOut is null
  or DaysToSellOut > DaysUntilDemandDrop
)
```

Allowed actions:

```text
ScaleGoodKeywords
IncreaseBidForScaleCandidates
ExpandRelevantKeywords
AcceptHigherDrrTemporarily
```

Forbidden actions:

```text
AggressivelyReduceAllSpend
DisableConvertingKeywords
```

## Goal weights

### ReduceDrr

```text
BadSpendWithoutOrders = 1.5
BadDrr = 1.4
CampaignEfficiencyProblem = 1.4
WatchCandidate = 0.9
ScaleCandidate = 0.8
StockRisk = 0.7
SeasonRisk = 0.7
default = 1.0
```

### IncreaseOrders

```text
ScaleCandidate = 1.5
PositionGrowthCandidate = 1.3
GoodKeyword = 1.3
StockRisk = 1.1
BadSpendWithoutOrders = 1.0
BadDrr = 0.9
LowData = 0.5
default = 1.0
```

### SellOutStock

```text
StockRisk = 1.7
SeasonRisk = 1.5
ScaleCandidate = 1.4
GoodKeyword = 1.3
PositionGrowthCandidate = 1.2
BadSpendWithoutOrders = 1.0
BadDrr = 0.8
WatchCandidate = 0.8
LowData = 0.5
default = 1.0
```

### IncreaseRevenue

```text
ScaleCandidate = 1.5
GoodKeyword = 1.4
PositionGrowthCandidate = 1.3
CampaignGrowthOpportunity = 1.3
BadSpendWithoutOrders = 0.9
BadDrr = 0.9
StockRisk = 1.0
default = 1.0
```

### MaintainPosition

```text
PositionGrowthCandidate = 1.5
GoodKeyword = 1.3
ScaleCandidate = 1.2
BadDrr = 0.9
BadSpendWithoutOrders = 0.8
StockRisk = 0.8
default = 1.0
```

## Impact score

For bad spend:

```text
Spend < 300 -> 0.2
300-999 -> 0.4
1000-2999 -> 0.7
>= 3000 -> 1.0
```

For scale candidates by orders:

```text
Orders < 3 -> 0.3
3-9 -> 0.6
10-29 -> 0.8
>= 30 -> 1.0
```

For stock risk:

```text
SalesPaceCoverage >= 1.0 -> 0.2
0.7-1.0 -> 0.5
0.4-0.7 -> 0.8
< 0.4 -> 1.0
AvgDailyOrders = 0 -> 1.0
```

Default impact score:

```text
0.5
```

## Priority score

Formula:

```text
RawPriorityScore =
GoalWeight
* ImpactScore
* UrgencyScore
* ConfidenceScore
* SeasonScore
* StockRiskScore
```

Final score:

```text
PriorityScore = min(100, round(RawPriorityScore * PriorityMultiplier))
```

Default `PriorityMultiplier`:

```text
25
```

Priority levels:

```text
0-29 -> Low
30-59 -> Medium
60-79 -> High
80-100 -> Critical
```

Overrides:

```text
StockRisk and DaysUntilDemandDrop <= 14 and SalesPaceCoverage < 0.7 -> at least High
StockRisk and DaysUntilDemandDrop <= 7 and SalesPaceCoverage < 0.7 -> at least Critical
BadSpendWithoutOrders and Spend >= 3000 and ConfidenceLevel = High -> at least High
```

## Guardrails

### Do not minus converting keyword

Condition:

```text
Orders > 0
and Drr is not null
and Drr <= TargetDrr * 1.2
```

Forbidden:

```text
ImmediateMinusKeyword
```

### Do not scale bad economy

Condition:

```text
Drr is not null
and Drr > TargetDrr * 1.5
```

Forbidden:

```text
Scale
AggressiveScale
```

### Do not aggressively cut spend during stock sell-out

Condition:

```text
Goal = SellOutStock
and StockRisk is High or Critical
```

Forbidden:

```text
AggressivelyReduceAllSpend
```

### Do not make hard conclusions with low data

Condition:

```text
ConfidenceLevel = Low
```

Forbidden:

```text
ImmediateMinusKeyword
AggressiveBidChange
Scale
AggressiveScale
```

## Top-N selection

Before LLM, select:

```text
Top 3 Critical
Top 5 High
Top 5 ScaleCandidate
Top 5 WatchCandidate
Top 5 LowData if needed
```

Then enforce:

```text
MaxInsightsForLlm = 20
```

Stable sorting:

```text
PriorityScore desc
PriorityLevel desc
InsightType asc
EntityType asc
EntityName asc
Id asc
```

## Reason codes

Use stable snake_case reason codes:

```text
significant_spend_without_orders
drr_above_target
drr_below_target
has_stable_orders
low_confidence
season_deadline_close
stock_will_not_sell_out_in_time
sales_pace_too_low
keyword_converts
keyword_semantically_suspicious
growth_candidate
watch_candidate
small_drr_deviation
not_enough_clicks
not_enough_spend
```

Do not use localized reason codes in backend data. Localized text belongs to LLM or frontend presentation.
