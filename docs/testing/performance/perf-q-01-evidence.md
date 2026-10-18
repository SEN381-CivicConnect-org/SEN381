# NFR-07 / PERF-Q-01: Staff Queue Query Performance Evidence

**Executed At:** 2026-10-17 11:46:48 UTC  
**Dataset Scale:** 20,000 total incidents (200 active/open, 19,800 resolved/closed/rejected)  
**Target Service Team:** Team ID 1  
**Iterations:** 100 consecutive runs  

## 1. Requirement & Traceability Specification

- **Requirement ID:** NFR-07 (Staff Queue Query Performance)
- **Test Identifier:** `PERF-Q-01 QueueP95AndPlan`
- **Source Document:** PED §2; §1; `docs/requirements/Non-Functional Requirements/non-functional-requirements-V1.0.html`
- **Design Reference:** Partial composite index `incident_team_queue_idx` on `(assigned_team_id, opened_at DESC) WHERE status IN ('NEW', 'ASSIGNED', 'IN_PROGRESS', 'ON_HOLD')`
- **Statement:** The system will return the first page of a service team's open incident queue within 500 ms at the 95th percentile, measured at the database, with a dataset representative of three years of operation.
- **Acceptance Criteria:** Given a database seeded with 20 000 incidents of which 200 are open, when the team queue query for one team is executed 100 times, then the 95th-percentile execution time is 500 ms or less and the query plan shows the partial team-queue index being used rather than a sequential scan.

## 2. PostgreSQL Query Planner Analysis (EXPLAIN ANALYZE)

The team queue query was evaluated after `VACUUM ANALYZE`:

```sql
EXPLAIN (ANALYZE, BUFFERS, COSTS, VERBOSE, FORMAT TEXT)
SELECT 
    i.id, i.reference_number, i.title, i.status, i.category_id, i.location_id,
    i.assigned_team_id, i.priority, i.opened_at, i.due_at, i.resolved_at, i.closed_at
FROM incident AS i
WHERE i.assigned_team_id = 1
  AND i.status IN ('NEW', 'ASSIGNED', 'IN_PROGRESS', 'ON_HOLD')
ORDER BY i.opened_at DESC
LIMIT 50 OFFSET 0;
```

### Execution Plan Tree

```text
Limit  (cost=115.47..115.55 rows=33 width=116) (actual time=0.057..0.061 rows=43 loops=1)
  Output: id, reference_number, title, status, category_id, location_id, assigned_team_id, priority, opened_at, due_at, resolved_at, closed_at
  Buffers: shared hit=8
  ->  Sort  (cost=115.47..115.55 rows=33 width=116) (actual time=0.056..0.058 rows=43 loops=1)
        Output: id, reference_number, title, status, category_id, location_id, assigned_team_id, priority, opened_at, due_at, resolved_at, closed_at
        Sort Key: i.opened_at DESC
        Sort Method: quicksort  Memory: 31kB
        Buffers: shared hit=8
        ->  Bitmap Heap Scan on public.incident i  (cost=4.40..114.64 rows=33 width=116) (actual time=0.025..0.039 rows=43 loops=1)
              Output: id, reference_number, title, status, category_id, location_id, assigned_team_id, priority, opened_at, due_at, resolved_at, closed_at
              Recheck Cond: ((i.assigned_team_id = 1) AND (i.status = ANY ('{NEW,ASSIGNED,IN_PROGRESS,ON_HOLD}'::incident_status[])))
              Heap Blocks: exact=7
              Buffers: shared hit=8
              ->  Bitmap Index Scan on incident_team_queue_idx  (cost=0.00..4.39 rows=33 width=0) (actual time=0.019..0.020 rows=43 loops=1)
                    Index Cond: (i.assigned_team_id = 1)
                    Buffers: shared hit=1
Planning:
  Buffers: shared hit=64
Planning Time: 0.393 ms
Execution Time: 0.117 ms
```

### Plan Inspection Verification

| Check | Expected | Actual | Pass/Fail |
| --- | --- | --- | --- |
| Partial Index Usage | `incident_team_queue_idx` | `->  Bitmap Index Scan on incident_team_queue_idx  (cost=0.00..4.39 rows=33 width=0) (actual time=0.019..0.020 rows=43 loops=1)` | PASS ✅ |
| Sequential Scan on incident | None (`0`) | Zero Seq Scan | PASS ✅ |
| PostgreSQL Direct Plan Time | < 500 ms | `0.117 ms` | PASS ✅ |

## 3. 100-Iteration Benchmark Distribution

| Percentile / Metric | Database Execution (Npgsql) | Application Round-Trip (EF Core) | NFR-07 Limit | Compliance |
| --- | --- | --- | --- | --- |
| Minimum | 1.045 ms | 2.092 ms | — | Informational |
| Mean (Average) | 1.449 ms | 2.764 ms | — | Informational |
| Median (p50) | 1.352 ms | 2.555 ms | — | Informational |
| 90th Percentile (p90) | 1.612 ms | 3.324 ms | — | Informational |
| **95th Percentile (p95)** | **1.681 ms** | **3.450 ms** | **≤ 500 ms** | **PASS ✅** |
| 99th Percentile (p99) | 1.998 ms | 4.112 ms | — | Informational |
| Maximum | 9.483 ms | 12.596 ms | — | Informational |

## 4. Formal Conclusion

- **Index Effectiveness**: The dedicated partial composite index `incident_team_queue_idx` completely eliminates sequential scans across 20,000 incident rows, allowing PostgreSQL to satisfy the queue query with a direct index scan in under **1.681 ms** at p95.
- **Compliance Status**: **100% compliant with NFR-07 and PERF-Q-01**. The measured database p95 of **1.681 ms** beats the 500 ms requirement with an order-of-magnitude performance headroom.

