---
target: Dashboard
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 3
target_identity: "file:D:\\git\\theburyproject1\\Views\\Dashboard\\Index.cshtml"
target_fingerprint: "sha256:2d86981d51c4180878d88d2663a90a9430f9a1c65f9519052c5497a4c7f777e9"
target_path: "D:\\git\\theburyproject1\\Views\\Dashboard\\Index.cshtml"
timestamp: 2026-10-05T16-45-52Z
slug: views-dashboard-index-cshtml
closed: true
---
Method: dual-agent (A: design review · B: detector + browser)

Dashboard (Views/Dashboard/Index.cshtml) — 25/40, Acceptable.
Heuristics: 1=3, 2=3, 3=3, 4=2, 5=3, 6=3, 7=2, 8=2, 9=2, 10=2.

Priority issues
- P1 One-voice (lime) rule violated: ~10 lime elements at 1440px, no real primary action (colorize/quieter)
- P1 Hierarchy: KPIs ~20px, no "do this first", Stock bajo repeated 4x, KPI cards not links (layout/bolder)
- P1 Token drift + sub-12px type: 17 text-[10/11px] (detector: design-system-font-size x17; browser undersized-ui-text x10), raw Tailwind colors, text-red-500 (typeset/polish)
- P2 No error/loading states; notes widget shows raw markdown; unlabeled textarea (harden)
- P2 Mobile: quick accesses at bottom, tabs ~28px (adapt)
Detector extras: low-contrast 1.7:1 (#fff on lime), skipped heading h2->h5, flat type hierarchy, cramped padding x2.
