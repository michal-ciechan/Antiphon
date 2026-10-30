Verification remains incomplete: 3557 fresh passes, 0 assertion failures, 43 expected Windows skips; CP-79 and CP-80 still need measured follow-up.

All fresh receipts below bind to clean source **b4cea3d0e1d3f9e47af86095696a72a25bcd530a**, fingerprint `d05cfb143e4dc1dface38b1ff7c91577eb92cdc0a04414f385e7b7427a947ab8`, and one verified isolated producer. The tracked tree stayed frozen throughout execution. This is ordinary Debug evidence, not Full, native qualification, ReviewedSourceClean, land approval or activation. No production/test source or definition changed.

### Fresh per-row observations

The latest completed observation replaces only the matching historical row. Raw timeout, roster-miss, cancellation and stopped-run records remain below. Direct-row execution is the monotonic driver wall envelope minus receipt admission; it is **not** scheduler-charged execution. Scheduled rows use the scheduler measurement. `T:` phases are TRX-only startup/body/teardown and exclude pre-TRX dotnet startup; `H:` phases include measured host startup. Detailed DB, CLI, fs, fixture, IO and drain spans are unknown when no phase was emitted. No unknown is represented as zero. Case p50/p95/max are nearest-rank across different cases in one run, not repeated row-history quantiles. The table selects one latest observation per identity; successful repeat histories for CP-69/74/81 are retained separately. Sample sizes remain small and bounds provisional.

| Row | Raw state | Executed / passed / failed / skipped | Wall s | Admission s | Execution s | Startup / body wall / teardown s | Case p50 / p95 / max s (n) | Actual bound min |
|---|---|---|---:|---:|---:|---|---|---:|
| CP-46 | green | 215 / 215 / 0 / 1 | 14.529 | 0.000 | 14.529 | T: 1.009 / 7.735 / 0.214 | 0.009 / 1.064 / 7.312 (215) | 15 |
| CP-52 | green | 247 / 247 / 0 / 2 | 9.426 | 0.000 | 9.426 | T: 1.744 / 0.588 / 0.317 | 0.003 / 0.248 / 0.509 (247) | 15 |
| CP-54 | green | 249 / 249 / 0 / 1 | 41.917 | 0.000 | 41.917 | T: 21.313 / 13.639 / 1.534 | 0.010 / 0.131 / 4.850 (249) | 15 |
| CP-55 | green | 244 / 244 / 0 / 1 | 7.385 | 0.000 | 7.385 | T: 1.819 / 0.301 / 0.338 | 0.003 / 0.166 / 0.206 (244) | 15 |
| CP-56 | green | 224 / 224 / 0 / 17 | 8.626 | 0.000 | 8.626 | T: 1.937 / 0.461 / 0.333 | 0.009 / 0.197 / 0.269 (224) | 15 |
| CP-62 | green | 238 / 238 / 0 / 4 | 8.292 | 0.000 | 8.292 | T: 1.930 / 0.388 / 0.337 | 0.016 / 0.150 / 0.210 (238) | 15 |
| CP-64 | green | 239 / 239 / 0 / 2 | 27.889 | 0.000 | 27.889 | T: 18.296 / 1.981 / 2.786 | 0.012 / 0.131 / 1.728 (239) | 15 |
| CP-67a | green | 38 / 38 / 0 / 0 | 106.792 | 90.000 | 16.506 | H: 8.970 / 2.681 / 0.990 | 0.248 / 2.328 / 2.336 (38) | 6 |
| CP-67b | green | 48 / 48 / 0 / 0 | 605.584 | 180.000 | 425.584 | T: 1.546 / 414.188 / 0.202 | 1.913 / 31.666 / 121.245 (48) | 15 |
| CP-67c | green | 40 / 40 / 0 / 0 | 321.923 | 240.000 | 81.444 | H: 5.532 / 72.547 / 0.693 | 0.090 / 10.951 / 22.775 (40) | 6 |
| CP-67d | green | 28 / 28 / 0 / 0 | 107.887 | 30.000 | 77.635 | H: 8.601 / 66.361 / 0.480 | 1.565 / 20.627 / 23.310 (28) | 6 |
| CP-67e | green | 61 / 61 / 0 / 1 | 544.994 | 225.000 | 319.994 | T: 1.821 / 307.916 / 0.734 | 1.011 / 27.864 / 50.539 (61) | 15 |
| CP-68 | green | 214 / 214 / 0 / 1 | 641.577 | 0.000 | 641.577 | T: 1.175 / 633.573 / 0.166 | 0.143 / 19.809 / 50.089 (214) | 15 |
| CP-69 | green | 188 / 188 / 0 / 8 | 305.804 | 30.000 | 275.804 | T: 0.909 / 268.268 / 0.163 | 0.930 / 24.350 / 267.716 (188) | 15 |
| CP-70 | green | 12 / 12 / 0 / 0 | 69.919 | 60.000 | 9.567 | H: 5.224 / 2.360 / 0.907 | 0.402 / 2.360 / 2.360 (12) | 4 |
| CP-71 | green | 61 / 61 / 0 / 0 | 6.483 | 0.000 | 6.396 | H: 5.023 / 0.069 / 0.523 | 0.005 / 0.016 / 0.069 (61) | 4 |
| CP-72 | green | 246 / 246 / 0 / 0 | 155.958 | 135.000 | 20.851 | H: 3.456 / 15.798 / 0.718 | 0.002 / 1.180 / 15.728 (246) | 8 |
| CP-73 | green | 248 / 248 / 0 / 0 | 109.007 | 90.000 | 18.877 | H: 5.857 / 10.664 / 1.185 | 0.053 / 2.807 / 7.389 (248) | 8 |
| CP-74 | green | 248 / 248 / 0 / 2 | 60.709 | 30.000 | 30.709 | T: 2.126 / 19.741 / 0.223 | 0.051 / 0.365 / 19.086 (248) | 15 |
| CP-75 | green | 5 / 5 / 0 / 0 | 6.522 | 0.000 | 6.424 | H: 4.357 / 0.021 / 0.538 | 0.021 / 0.021 / 0.021 (5) | 4 |
| CP-76 | green | 46 / 46 / 0 / 0 | 17.615 | 0.000 | 17.124 | H: 4.664 / 10.296 / 1.355 | 0.029 / 0.619 / 7.677 (46) | 4 |
| CP-77 | green | 4 / 4 / 0 / 0 | 72.373 | 0.000 | 72.016 | H: 66.967 / 1.604 / 2.550 | 0.021 / 1.604 / 1.604 (4) | 4 |
| CP-78 | green | 65 / 65 / 0 / 2 | 878.178 | 90.000 | 788.178 | T: 1.005 / 779.595 / 0.160 | 2.473 / 108.491 / 142.368 (65) | 15 |
| CP-79 | running | unknown / unknown / unknown / unknown | unknown | 45.000 | unknown | unknown | unknown | 24 |
| CP-80 | timeout | unknown / unknown / unknown / unknown | 979.915 | 75.000 | 904.915 | unknown | unknown | 15 |
| CP-81 | green | 249 / 249 / 0 / 1 | 157.662 | 15.000 | 142.662 | T: 18.979 / 116.654 / 1.380 | 0.034 / 4.935 / 12.154 (249) | 15 |
| CP-82 | green | 37 / 37 / 0 / 0 | 150.181 | 120.000 | 30.181 | T: 0.633 / 22.921 / 0.355 | 0.018 / 6.027 / 9.107 (37) | 15 |
| CP-83 | green | 1 / 1 / 0 / 0 | 38.131 | 30.000 | 8.131 | T: 0.492 / 1.840 / 0.311 | 1.839 / 1.839 / 1.839 (1) | 15 |
| CP-84 | green | 8 / 8 / 0 / 0 | 20.126 | 15.000 | 5.126 | T: 0.500 / 0.010 / 0.186 | 0.010 / 0.010 / 0.010 (8) | 15 |
| CP-85 | green | 2 / 2 / 0 / 0 | 227.186 | 195.000 | 32.186 | T: 16.860 / 5.842 / 3.254 | 5.827 / 5.841 / 5.841 (2) | 15 |
| CP-86 | green | 9 / 9 / 0 / 0 | 302.975 | 135.000 | 167.975 | T: 29.961 / 123.194 / 3.571 | 13.549 / 27.800 / 27.800 (9) | 15 |
| CP-87 | green | 1 / 1 / 0 / 0 | 118.704 | 90.000 | 28.704 | T: 15.223 / 5.076 / 3.353 | 5.077 / 5.077 / 5.077 (1) | 15 |
| CP-88 | green | 2 / 2 / 0 / 0 | 106.872 | 30.000 | 76.872 | T: 25.771 / 36.964 / 4.796 | 15.488 / 21.421 / 21.421 (2) | 15 |
| CP-89 | green | 27 / 27 / 0 / 0 | 132.731 | 45.000 | 87.731 | T: 30.616 / 38.170 / 11.207 | 37.095 / 37.237 / 38.169 (27) | 15 |
| CP-90 | green | 11 / 11 / 0 / 0 | 287.772 | 240.000 | 47.772 | T: 25.904 / 12.536 / 3.188 | 12.151 / 12.532 / 12.532 (11) | 15 |
| CP-91 | green | 2 / 2 / 0 / 0 | 29.926 | 0.000 | 29.926 | T: 15.516 / 3.651 / 3.277 | 3.302 / 3.651 / 3.651 (2) | 15 |

CP-67 is covered by the union of five disjoint class partitions: 38+48+40+28+62 discovered = **216**, with 215 passed and one explicit Windows skip. The original full aggregate filter is retained in the plan history; it was not repeated as a duplicate run. CP-73 actually discovered/passed 248 versus its old floor 247; this is recorded rather than credited as an exact old census. The earlier CP-69 and CP-74 raw roster-miss states reflect exactly 8 and 2 deliberate Windows guards against old unadjusted floors, with no failed tests. Their corrected planned floors do not rewrite the historical receipts.

### Prior source-qualified observations retained

These unchanged observations were executed by earlier tasks, not rerun or rebound to b4cea3d0. Full SHA mappings and complete old histories remain in `.antiphon/task-40e35f61.md`. The following rows plus the fresh table form the S2/S3 ledger; no single aggregate run finished.

| Row | Source prefix | State | Executed / passed / failed / skipped | Wall s | Admission s | Execution s | Startup / body wall / teardown s | Host s | Remainder s | Case p95 / max s (n) | Bound min |
|---|---|---|---|---:|---:|---:|---|---:|---:|---|---:|
| CP-17 | 192f642b2 | green | 22 / 22 / 0 / 0 | 133.003 | 45.000 | 87.703 | 31.668 / 49.089 / 6.108 | 86.865 | 0.838 | 2.702 / 18.531 (22) | 10 |
| CP-28 | 192f642b2 | green | 382 / 382 / 0 / 0 | 95.556 | 0.000 | 95.470 | 25.840 / 65.141 / 3.638 | 94.620 | 0.849 | 1.942 / 10.851 (382) | 10 |
| CP-16 | 192f642b2 | green | 44 / 44 / 0 / 0 | 176.705 | 75.000 | 101.613 | 35.732 / 32.454 / 4.117 | 72.302 | 29.310 | 1.143 / 10.643 (44) | 6 |
| CP-15 | d36e79f43 | green | 12 / 12 / 0 / 0 | 6.343 | 0.000 | 6.299 | 4.679 / 0.032 / 0.774 | 5.485 | 0.814 | 0.032 / 0.032 (12) | 10 |
| CP-19 | d36e79f43 | green | 22 / 22 / 0 / 0 | 41.439 | 0.000 | 41.330 | 20.271 / 16.570 / 3.446 | 40.286 | 1.043 | 0.913 / 8.294 (22) | 4 |
| CP-21 | d36e79f43 | green | 48 / 48 / 0 / 0 | 78.574 | 0.000 | 78.506 | 24.869 / 49.073 / 3.651 | 77.593 | 0.913 | 2.111 / 13.114 (48) | 8 |
| CP-22 | 192f642b2 | green | 36 / 36 / 0 / 0 | 187.876 | 150.000 | 37.739 | 18.714 / 15.200 / 3.053 | 36.968 | 0.771 | 0.860 / 6.693 (36) | 8 |
| CP-23 | 192f642b2 | green | 167 / 167 / 0 / 0 | 116.204 | 45.000 | 71.098 | 20.347 / 46.490 / 3.670 | 70.506 | 0.592 | 21.136 / 25.080 (167) | 24 |
| CP-24 | d36e79f43 | green | 10 / 10 / 0 / 0 | 59.981 | 15.000 | 44.745 | 33.777 / 4.165 / 5.100 | 43.043 | 1.703 | 4.165 / 4.165 (10) | 4 |
| CP-25 | d36e79f43 | green | 39 / 39 / 0 / 0 | 98.299 | 45.000 | 52.892 | 31.509 / 12.687 / 4.830 | 49.026 | 3.866 | 12.657 / 12.686 (39) | 6 |
| CP-26 | 192f642b2 | green | 59 / 59 / 0 / 0 | 92.517 | 15.000 | 77.423 | 58.147 / 14.351 / 4.378 | 76.877 | 0.546 | 5.015 / 5.033 (59) | 8 |
| CP-27 | d36e79f43 | green | 341 / 341 / 0 / 0 | 257.181 | 0.000 | 256.686 | 39.361 / 193.366 / 4.104 | 236.831 | 19.855 | 1.393 / 8.871 (341) | 10 |
| CP-29 | d36e79f43 | green | 17 / 17 / 0 / 0 | 62.850 | 15.000 | 47.692 | 21.674 / 18.690 / 6.293 | 46.657 | 1.035 | 18.690 / 18.690 (17) | 6 |
| CP-30 | d36e79f43 | green | 28 / 28 / 0 / 0 | 69.401 | 0.000 | 69.284 | 31.276 / 32.199 / 2.477 | 65.952 | 3.332 | 11.390 / 12.728 (28) | 6 |
| CP-31 | d36e79f43 | green | 35 / 35 / 0 / 0 | 197.813 | 0.000 | 197.760 | 32.083 / 159.748 / 4.479 | 196.309 | 1.451 | 23.812 / 27.195 (35) | 6 |
| CP-32 | d36e79f43 | green | 12 / 12 / 0 / 0 | 98.834 | 0.000 | 98.146 | 32.323 / 39.746 / 2.786 | 74.855 | 23.291 | 19.593 / 19.593 (12) | 4 |
| CP-33 | d36e79f43 | green | 4 / 4 / 0 / 0 | 49.315 | 0.000 | 49.243 | 27.354 / 17.152 / 3.422 | 47.928 | 1.315 | 10.791 / 10.791 (4) | 2 |
| CP-34 | d36e79f43 | green | 8 / 8 / 0 / 0 | 45.088 | 0.000 | 44.978 | 20.319 / 16.427 / 4.431 | 41.177 | 3.801 | 4.978 / 4.978 (8) | 4 |
| CP-35 | d36e79f43 | green | 25 / 25 / 0 / 0 | 61.353 | 30.000 | 31.237 | 19.580 / 4.769 / 3.989 | 28.339 | 2.898 | 4.512 / 4.769 (25) | 6 |
| CP-36 | d36e79f43 | green | 69 / 69 / 0 / 0 | 41.088 | 0.000 | 41.007 | 21.697 / 16.541 / 1.984 | 40.222 | 0.785 | 3.818 / 3.858 (69) | 8 |
| CP-37 | d36e79f43 | green | 1 / 1 / 0 / 0 | 36.225 | 0.000 | 36.110 | 26.412 / 4.928 / 3.148 | 34.488 | 1.623 | 4.928 / 4.928 (1) | 2 |
| CP-38 | d36e79f43 | green | 13 / 13 / 0 / 0 | 53.888 | 0.000 | 53.806 | 33.097 / 14.268 / 4.327 | 51.692 | 2.114 | 10.063 / 10.063 (13) | 4 |
| CP-39 | d36e79f43 | green | 1 / 1 / 0 / 0 | 46.626 | 0.000 | 45.987 | 23.822 / 17.549 / 3.390 | 44.761 | 1.226 | 17.549 / 17.549 (1) | 4 |
| CP-40 | d36e79f43 | green | 40 / 40 / 0 / 0 | 142.716 | 75.000 | 67.241 | 22.251 / 42.160 / 1.878 | 66.290 | 0.951 | 1.830 / 20.397 (40) | 10 |
| CP-41 | d36e79f43 | green | 7 / 7 / 0 / 0 | 33.811 | 0.000 | 33.718 | 22.076 / 9.277 / 1.379 | 32.732 | 0.986 | 5.451 / 5.451 (7) | 4 |
| CP-42 | d36e79f43 | green | 12 / 12 / 0 / 0 | 102.383 | 0.000 | 102.315 | 20.407 / 78.018 / 2.191 | 100.617 | 1.698 | 8.665 / 8.665 (12) | 6 |
| CP-43 | d36e79f43 | green | 10 / 10 / 0 / 0 | 43.757 | 0.000 | 43.681 | 32.750 / 5.806 / 3.775 | 42.332 | 1.350 | 5.806 / 5.806 (10) | 4 |
| CP-44 | d36e79f43 | green | 1 / 1 / 0 / 0 | 88.540 | 30.000 | 58.310 | 5.764 / 44.277 / 5.411 | 55.452 | 2.857 | 44.277 / 44.277 (1) | 2 |
| CP-45 | d36e79f43 | green | 46 / 46 / 0 / 0 | 122.142 | 105.000 | 16.968 | 7.346 / 6.968 / 0.927 | 15.241 | 1.727 | 0.532 / 6.967 (46) | 4 |
| CP-47 | d36e79f43 | green | 29 / 29 / 0 / 0 | 101.136 | 90.000 | 10.991 | 4.708 / 3.939 / 0.866 | 9.513 | 1.479 | 3.824 / 3.937 (29) | 4 |
| CP-48 | d36e79f43 | green | 22 / 22 / 0 / 0 | 28.356 | 15.000 | 13.165 | 5.081 / 5.304 / 1.026 | 11.411 | 1.754 | 5.131 / 5.303 (22) | 4 |
| CP-49 | 192f642b2 | green | 107 / 107 / 0 / 0 | 158.013 | 150.000 | 7.660 | 3.266 / 3.118 / 0.505 | 6.889 | 0.771 | 1.639 / 3.118 (107) | 4 |
| CP-50 | 192f642b2 | green | 158 / 158 / 0 / 0 | 7.955 | 0.000 | 7.885 | 4.260 / 2.507 / 0.473 | 7.240 | 0.645 | 1.938 / 2.275 (158) | 4 |
| CP-51 | 192f642b2 | green | 89 / 89 / 0 / 0 | 4.616 | 0.000 | 4.558 | 3.232 / 0.080 / 0.509 | 3.821 | 0.737 | 0.037 / 0.052 (89) | 4 |
| CP-53 | 192f642b2 | green | 173 / 173 / 0 / 0 | 7.296 | 0.000 | 7.221 | 5.562 / 0.319 / 0.726 | 6.606 | 0.615 | 0.071 / 0.169 (173) | 6 |
| CP-57 | 192f642b2 | green | 214 / 214 / 0 / 0 | 12.309 | 0.000 | 12.218 | 5.163 / 5.022 / 1.274 | 11.460 | 0.759 | 0.176 / 5.022 (214) | 6 |
| CP-58 | 192f642b2 | green | 184 / 184 / 0 / 0 | 6.997 | 0.000 | 6.899 | 4.878 / 0.406 / 0.792 | 6.075 | 0.824 | 0.111 / 0.257 (184) | 6 |
| CP-59 | 192f642b2 | green | 184 / 184 / 0 / 0 | 9.602 | 0.000 | 9.501 | 6.012 / 1.385 / 1.216 | 8.613 | 0.888 | 0.136 / 1.319 (184) | 6 |
| CP-60 | 192f642b2 | green | 35 / 35 / 0 / 0 | 10.111 | 0.000 | 9.986 | 7.247 / 0.756 / 0.846 | 8.848 | 1.138 | 0.406 / 0.449 (35) | 4 |
| CP-61 | 192f642b2 | green | 247 / 247 / 0 / 0 | 13.453 | 0.000 | 13.269 | 8.192 / 2.711 / 1.084 | 11.987 | 1.282 | 0.889 / 2.029 (247) | 8 |
| CP-63 | 192f642b2 | green | 241 / 241 / 0 / 0 | 14.949 | 0.000 | 14.816 | 10.537 / 2.679 / 0.610 | 13.826 | 0.990 | 0.386 / 2.210 (241) | 8 |
| CP-65 | 192f642b2 | green | 232 / 232 / 0 / 0 | 23.396 | 0.000 | 23.334 | 7.618 / 13.518 / 1.284 | 22.420 | 0.914 | 0.348 / 12.965 (232) | 6 |
| CP-66 | 192f642b2 | green | 89 / 89 / 0 / 0 | 13.781 | 0.000 | 13.386 | 8.369 / 2.627 / 0.817 | 11.813 | 1.573 | 1.726 / 1.734 (89) | 4 |
| CP-92 | 192f642b2 | green | 6 / 6 / 0 / 0 | 125.676 | 75.000 | 50.552 | 34.143 / 7.003 / 7.740 | 48.887 | 1.666 | 5.106 / 5.106 (6) | 4 |

### All fresh platform skips

All are deliberate **Windows-only proofs**, not Linux-capable gaps; no assertion, tag, timer or allowlist was weakened. The initial 28 expanded cases were inspected before execution and are classified individually in the investigation’s pre-run section; the complete fresh list is below. Fresh execution reproduced those exact 28 and found 15 additional deliberate platform guards. Native execution remains pending outside this commission.

| Row | Full class / expanded case | Classification | Runtime reason |
|---|---|---|---|
| CP-46 | Antiphon.Tests.Agents.AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine | Expected Windows-only | Skipped: The npm shim layout is Windows-specific |
| CP-52 | Antiphon.Tests.Application.AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone | Expected Windows-only | Skipped: Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows |
| CP-52 | Antiphon.Tests.Application.AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash | Expected Windows-only | Skipped: Canonical pin cwd is a Windows drive-rooted path |
| CP-54 | Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests.Off_settings_path_round_trips_through_LaunchArgvGuard | Expected Windows-only | Skipped: CommandLineToArgvW is Windows-only |
| CP-55 | Antiphon.Tests.Application.DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms | Expected Windows-only | Skipped: Drive-letter report paths resolve only against a Windows repo root |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.prefix_returns_matching_child_directories | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.existing_path_reports_exists_true | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.caches_within_ttl_and_refreshes_after | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.partial_leaf_matches_substring_within_child_name | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.DirectoryBrowseServiceTests.trailing_slash_lists_children_of_that_directory | Expected Windows-only | Skipped: Drive-letter directory listing needs a Windows MockFileSystem |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(cr) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(nul) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(lf) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(crlf) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_first) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_second) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(missing) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(alias) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(equals) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(braced_env) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-56 | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env_flag) | Expected Windows-only | Skipped: CARD-0382 Grok rules argv guard is Windows-only by design |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.Unknown_runner_backend_downgrades_modern_server | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_inbox_conhost_downgrades_a_modern_server | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_runner_on_the_modern_backend_confirms_the_raised_ceilings | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-62 | Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_silent_runner_leaves_this_processes_own_decision_standing | Expected Windows-only | Skipped: Windows delivery profile proof |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Grok_never_uses_local_modern_evidence | Expected Windows-only | Skipped: ConPTY only on Windows |
| CP-64 | Antiphon.Tests.Application.SessionDeliveryProfileTests.Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind | Expected Windows-only | Skipped: ConPTY only on Windows |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset | Expected Windows-only | Skipped: CARD-1310 confined Windows build leaves UseAppHost unset |
| CP-68 | Antiphon.Tests.Checkpoints.EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open | Expected Windows-only | Skipped: Native file-sharing retry proof requires Windows. |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_sharing_violation_retries_then_adopts | Expected Windows-only | Skipped: CARD-1318 Windows custody hardening |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_copied_old_change_time_is_adopted | Expected Windows-only | Skipped: CARD-1318 Windows copy change time |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write | Expected Windows-only | Skipped: CARD-1310 Windows output custody |
| CP-69 | Antiphon.Tests.Checkpoints.OutputCleanupTests.windows_set_clock_and_foreign_dacl_are_not_adopted | Expected Windows-only | Skipped: CARD-1318 Windows custody hardening |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_quick_row_finishes_beside_a_slow_row | Expected Windows-only | Skipped: Concurrent Windows row processes must not inherit each other's stdout pipes. |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_chatty_row_drains_interleaved_stdout_and_stderr | Expected Windows-only | Skipped: Windows row pipes must drain a large interleaved stdout and stderr. |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_timeout_kills_the_start_b_grandchild | Expected Windows-only | Skipped: The descendant sweep kills a start /b grandchild on Windows. |
| CP-69 | Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_arguments_round_trip_intact | Expected Windows-only | Skipped: Windows row arguments are delivered by ProcessStartInfo.ArgumentList. |
| CP-74 | Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests.C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded | Expected Windows-only | Skipped: Sharing-mode locks are a Windows file-system behaviour. |
| CP-74 | Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests.C665_LockedFileMidDeleteResumesOnLaterPass | Expected Windows-only | Skipped: Sharing-mode locks are a Windows file-system behaviour. |
| CP-78 | Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact | Expected Windows-only | Skipped: CARD-1050 and CARD-1110: this audit needs Windows placement |
| CP-78 | Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact | Expected Windows-only | Skipped: CARD-1050 and CARD-1110: this audit needs Windows placement |
| CP-81 | Antiphon.Tests.TestHelpers.TestPhaseSummaryTests.C1514T_PS51AndRerun | Expected Windows-only | Skipped: CARD-1514: actual nightly native custody and PowerShell 5.1 rerun require Windows; Linux cannot qualify this owner. |

`RowRunner` accepts portable minima and preserves skipped identities. `tools/Antiphon.Checkpoints/Report/ReportValidator.cs` additionally requires `row.Skipped == 0`, even for explicitly planned Windows skips. CP-67b validation passed; CP-67e validation returned exit2 `CHECKPOINT SOURCE INVALID reason=row_failed`. Keep this tool gate incomplete and retain native obligations; do not transform a Linux skip into execution credit. Propose a scoped tool-contract follow-up preserving identity/count/source checks; no card was filed.

### CP-67 measured diagnosis and corrected scheduling

The inherited aggregate six-minute timeout had no per-case progress or attribution. Before repeating any selection, the plan was amended/committed with five disjoint partitions, finite six-minute diagnostics and early-start/late-complete journals. CP-67a/c/d finished. CP-67b timed out after 33 completed cases / 34 starts, in the second long bounded-reader probe; CP-67e expired late in its receipt matrix. No assertion failed, and an incomplete journal is not a red test. On the same frozen producer, standalone tool rows with their finite15-minute default completed b/e. The CLI `row` path exposes no per-row deadline switch, so that default is a disclosed scheduling exception; no per-test timeout was changed.

CP-67b’s two slowest `changed_readers_bound_fifo_and_exited_child_pipes` arguments took 121.245s and 121.049s. Its full case window was 414.188s. CP-67e spent 307.916s in its case window; the shape/verb matrix took 50.539s, transitive imported build 39.748s and conditional import 29.703s. These tests deliberately recreate receipts/output/process custody; sharing mutable fixtures would compromise their subject. Existing TempDir sweep is throttled at five minutes, so it is not established as the dominant repeated cost. No speculative fixture cache was introduced. Child/process/fs spans were not emitted by these existing tests: per-case wall is measured, attribution remains unknown.

The completed partition execution envelopes sum to about921.163s before admission. That makes the old 360s aggregate deadline demonstrably too short. A duplicate aggregate is unnecessary: the final plan uses the same exact class union through CP-67a..e and retains the original filter/history. Future initial bounds are a:6/b:16/c:6/d:6/e:12 minutes (2x integer estimates 3/8/3/3/6). b’s 16 minutes exceeds 2x425.584s; e’s 12 exceeds 2x319.994s. These are single-observation provisional bounds, not an invented row-history p95. CP-68’s641.577s supports estimate 11 / bound 22min; CP-69’s304.732s supports 6 /12; CP-78’s788.178s supports 14 /28. The successful standalone15-minute rows are reported at their actual bounds, not retroactively relabeled22/28. Product phase defaults 20/90min and test-specific bounds remain unchanged.

### Incomplete heavy selections and next execution

CP-79 `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/*` (499 identities) did not finish. Its `C1008_Recycle_resume_requires_matching_receipt` alone passed at 398.283s: 16 `C1008HostFixture` constructions each derive 4 Node compose models (64 constructor processes) plus 32 real offline fault/resume script invocations. The test uses 120s bounded child runs; whole class has 600s per-test caps. After about19 minutes only a small fraction of499 cases had started. Supported `stop --run 20261030-184126-5d9d` ended the executor before the remaining global execution budget could produce a whole receipt. It returned0; observer ended6 (`executor died without phase=done`). Main state is `stopped`, CP-79’s stale row state is `running`, no completed TRX or aggregate report exists. A transient TOOL CLEANUP IOException was followed by `Removed ... reason=identity-dead`; executor PID27470 was absent. This is incomplete coverage, never a passing row or assertion-red inheritance claim.

CP-80 retains its exact168-identity filter in the plan. The opt-in journal shows `RetiredTempContainerHostTests.C994_Each_removal_rechecks_live_proofs` constructing16 fixtures across before-first/late-fault matrices; it shares CP-79’s expensive host model setup. Its terminal outcome and journal counts are below. Before repeating either heavy row, commission measured Debug/Code: isolate immutable compose-model derivation once per process/run, keep per-case mutable state and fresh safety barriers, or split the complete unchanged identity set into bounded method partitions. Capture fixture/CLI/fs/process spans and preserve all 499+168 identities and protective assertions. Do not blindly rerun the full classes or widen test caps. No master-inherited assertion failure was claimed, so no scratch-master execution was fabricated.

### Runtime invariant witnesses and schema

| Invariant | Evidence and scope |
|---|---|
| Detection/hold cannot kill a Working session | No production code changed here. Inspection of the inherited hold, notice, typed-decision and late-forward paths found no added stop/kill/release; explicit Cancel/Retry stays explicit. Fresh CP-90 passed11 narrow CARD-0079 stop/restart witnesses, including human/provider/model/quota/auth holds and unsupported-stop refusal; CP-85..91 passed54 in total. CP-88 kills only its own crash-test worker, not product Working sessions. |
| Held tasks count everywhere | Held remains status Working; unchanged DispatchConcurrencyPolicy.IsOpen/IsParallel, dispatcher local-active and pipeline/Check selectors retain Working. Fresh CP-56 executes the policy classes with224 passes; historical CP-16 passed44 at192f642b2b0a5d23f9f9e33112adb8c43efe579d and CP-34/35/36 passed8/25/69 atd36e79f432307ff26ab687d06bad5e9a5f92cff1. Those are retained historical witnesses, not rerun source stamps. |
| Phase beats ceiling;20/90 unchanged | DelegationSettings defaults remain ModelWaitDeadlineMinutes20 and LocalExecutionDeadlineMinutes90; TaskDeadlinePolicy retains phase-first verdict semantics under S2/S3. Historical CP-16/22/25 hold, overdue and deadline cases remain source qualified in prior table. |
| CARD-1604 token_count unchanged | TaskDeadlinePolicy takes the later transcript/token_count activity only for ModelWait and caps by task age; no new transcript row, boot/local-tool/role-clock change. Source comparison with the combined pre-S2/S3 base confirms equivalent extraction. Existing CP-25 witness is historical. |
| S1 unchanged | Zero-row and uncertain Working retention / FailedTaskSessionOwnership were not changed here. Historical CP-19/21 passed22/48 atd36e79f432307ff26ab687d06bad5e9a5f92cff1. Fresh compaction recovery/receipt/control tests do not replace S1’s specific historical witnesses. |

Both inherited migrations are **additive, not entirely nullable**. `20261030114738_AddTaskCeilingSupervision` adds nullable hold fields, nonnullable Boolean CeilingOperatorExtended default false, and TaskSupervisionNotices with required and optional columns. `20261030143219_AddTaskSupervisionQueuePurpose` adds nullable SourceTaskSupervisionNoticeId plus its filtered unique index. Port S2+S3 together onto master/rc with both migrations, designers and snapshot; migrate before activation after joint ordinary Review and separately reviewed rc ports. No production migration ran. S4 remains behind CARD-1615 and CARD-1609. No tags, deployment or activation is authorized by this record.

### Producer, source and execution provenance

Base 0219ccebf3982b191116dd7d731c8d5b0aaa97c1; initial plan/skip diagnosis commit b4cea3d0e1d3f9e47af86095696a72a25bcd530a. Tool bootstrap used a granted host slot, UseAppHost=false,7.17s compiler/5 existing warnings/0 errors. Plan import admitted98 rows. Producer run20261030-184126-5d9d compiled once into tests/Antiphon.Tests/bin-c1610-p2: compiler325.33s /1419 warnings /0 errors; recorded build 390.337s, wall 438.572s, admission 30s, finite 24min coupled build bound. All subsequent selections reused that source-verified producer. Full inherited filters were compared byte-for-byte with the previous report. No broader assemblies or live/native runner tests were used.

Imported manifest is retained at `.antiphon/c1610-p2/import.yaml`; derived 36-row manifest `.antiphon/c1610-p2/run.yaml`, SHA256 `3d83b7e43d5aba86e6f0b877b53781ccc495b3b3b4e33d73511fb07fb4b281a2`, serial scheduling, UseAppHost=false, TEMP=/tmp,25min admission, finite 60min execution / 110min wall. The last persisted stopped-run budget snapshot (execution 2397.936s, wall 3403.253s, admission 1005.317s) predates most CP-79 work and is **not a final cost**. Standalone row runs continued the exact closed-list selections under their own granted build slots; at most two independently isolated Antiphon.Tests hosts overlapped, no Pty assembly co-scheduled, and all were joined before source edits. TestHelpers fixture lifecycle/cache selections use fake/isolated test schema operations, separate from the compaction integration fixtures. Slot waits were observed, not bypassed.

The actual land graph `-Check` passed at b4cea3d0, using the producer-staged LandGraphAudit DLL under a granted slot: two evaluations and approved canary, no `-Update`. Digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`,68 nodes,195s admission / 32s lease; canary `/work/worktrees/antiphon-landgraph-canary-aa49ced174df4cac828cb46b59b60e64`. Static direct ProjectReference inventory contains 50 projects and computed transitive dependents in ignored project-graph.json. Conditions/imports/dynamic edges are not qualified, and no rc-scoped credit is claimed.

Initial malformed expected-SHA attempt was refused before build/tests and immediately corrected; no red credit. CP-78’s first standalone admission was canceled and joined before tests began when its second Windows skip was discovered; corrected minimum 65 was used on the successful row. The canceled acquisition cost 71.159s and produced no test receipt. Evidence is retained; these are execution corrections, not product failures.

### Latest unedited CHECKPOINT lines

```text
CHECKPOINT CP-46 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Agents/(AgentLaunchSpecTests*)|(AgentProtocolAdapterFactoryTests*)|(AgentRegistrySettingsTests*)|(AgentRegistryTests*)|(AgentSessionSettingsTests*)|(CodexAdapterRawResponseTests*)|(CodexResponseAnalyzerTests*)|(CodexWorkingIndicatorTests*)|(FakeLlmApiSelfTests*)|(GrokAdapterTests*)|(GrokCredentialStoreTests*)|(GrokFreshWorktreeQualificationTests*)|(GrokLinuxBlockingPromptTests*)|(GrokLinuxStartupReadinessTests*)|(GrokModelListParserTests*)|(GrokSignInPromptDetectorTests*)|(GrokStartupCaptureStoreTests*)|(GrokStartupReadinessTests*)|(GrokStartupSizeTests*)|(GrokTrustPromptDetectorTests*)|(HerdrEnvelopeBodyTests*)|(OpenCodeAdapterTests*)|(RunnerClaudeAdapterEffortPromptTests*)|(RunnerClaudeAdapterTrustPromptTests*)|(RunnerClaudeAdapterVerifiedPromptTests*)/* executed=215 passed=215 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-46-20261030-202036-c176/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)|(AgentContextContractTests*)|(AgentExecutableResolverTests*)|(AgentPinPathTests*)|(AgentPresetsTests*)|(AgentSessionRuntimeBoundedReadTests*)|(AgentTaskDecisionQuestionTests*)|(AgentTaskDispatcherPredicateTests*)|(AgentTaskLandingStateTests*)|(AgentTaskLandQueueVisibilityTests*)|(AgentTaskLandVerificationEvidenceTests*)|(AgentTaskLivenessTests*)|(AlarmSettingsValidatorTests*)/* executed=247 passed=247 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-52-20261030-202051-89a5/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(ChannelOutboundContractTests*)|(ChannelOutboundEvidenceAccountingTests*)|(ChannelOutboundStorageTests*)|(ChannelPromptCorrelationUnitTests*)|(ChannelReplyAttachmentReaderTests*)|(CheckCompactionContinuationSettingsTests*)|(CheckInterpretationValidatorTests*)|(CheckpointManifestDocumentationTests*)|(CheckScheduleBackoffTests*)|(CheckScheduleElapsedTimesTests*)|(CheckScheduleRoundingTests*)|(CheckSpecialistLaunchPolicyTests*)|(ClaudeEffortByRoleSettingsTests*)|(ClaudeLaunchArgsTests*)|(ClaudeRemoteControlLaunchArgsTests*)|(CodexLaunchArgsTests*)|(CodexPhoneHomeProjectionTests*)|(ColumnTextTests*)|(CommitOnSettleDocumentationTests*)|(CommitOnSettlePolicyResolverTests*)|(CommitRecoveryObligationsTests*)|(CompactionContinuationPolicyTests*)|(CompletionHeaderOverlapTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-54-20261030-202100-1061/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(CompletionNoteWorkQueueTests*)|(ComplexityEffectiveComposeTests*)|(ComplexityRoutingComposeTests*)|(ContextCompactionSettingsTests*)|(D2AdvisoryDocumentationTests*)|(DelegateBindRefusalRecoveryTests*)|(DelegateBranchNameTests*)|(DelegationAllowedRootsFileTests*)|(DelegationCapabilityContractTests*)|(DelegationCostTests*)|(DelegationDenyHookPolicyTests*)|(DelegationKindPricingTests*)|(DelegationLeaseSettingsTests*)|(DelegationPoolDefaultsTests*)|(DelegationQuestionDetectionTests*)|(DelegationReportFormatterTests*)|(DelegationScopeLeaseTests*)|(DelegationWorkspaceBoundaryTests*)|(DeliveryBackendCeilingsTests*)/* executed=244 passed=244 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-55-20261030-202142-4e11/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(DesktopCapabilitiesSnapshotTests*)|(DiagnosisTests*)|(DiagnosticsRedactorTests*)|(DialogDeliveryAdmissionTests*)|(DirectoryBrowseServiceTests*)|(DirectoryMatcherTests*)|(DispatchBaseWarningRosterTests*)|(DispatchConcurrencyPolicyTests*)|(DispatchHoldLedgerTests*)|(ExpectationDirectiveTests*)|(ExpectationPipelineTests*)|(GitIndexLockTests*)|(GrokComposerScreenTests*)|(GrokDeliveryShapeTests*)|(GrokLaunchArgsTests*)|(GrokNativeSessionResumeTests*)|(GrokQuestionPopupTests*)|(GrokRulesLiveEvidenceTests*)|(GrokRulesTransportCompatibilityTests*)|(HerdrAgentKindMapTests*)|(HerdrLaunchContextTitleTests*)|(HerdrStatusDisagreementTests*)/* executed=224 passed=224 failed=0 skipped=17 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-56-20261030-202149-86ba/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-62 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(PolicyRefreshDeltaTests*)|(PostLandMutationContractTests*)|(PostLandMutationPublicationTests*)|(PostLandMutationReceiptPolicyTests*)|(PostLandRetrospectiveContractTests*)|(ProviderCapacityNoticeTests*)|(ProviderContractCatalogTests*)|(ProviderEndpointDocumentationTests*)|(ProviderQuotaRefusalTests*)|(PtyDeliveryCeilingsTests*)|(PtyInlineCeilingTests*)|(PublishedRoutingDocumentationTests*)|(ReceiptScanDestinationTests*)|(ReleaseBranchModelDocumentationTests*)|(RemoteControlMenuScreenTests*)|(RemoteControlPolicyTests*)|(RemoteVerificationWorkspaceTests*)/* executed=238 passed=238 failed=0 skipped=4 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-62-20261030-202158-f25c/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-64 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/(SessionContextUsageTests*)|(SessionDeliveryProfileTests*)|(SessionReAdoptionStateTests*)|(SessionRunnerAbsenceEvidenceDocumentationTests*)|(SessionRunnerHttpClientHostStatsTests*)|(SessionRunnerSettingsTests*)|(SettlementSyncDebtAttentionTests*)|(SettlementSyncDebtPolicyTests*)|(SharedWriterLeaseProjectionTests*)|(SiblingInspectionResultsTests*)|(SiblingWarningReducerTests*)|(SlashCommandCatalogServiceTests*)|(SlashCommandParserTests*)|(SourceLandingAdmissionTests*)|(SpecialistAttemptEvidenceTests*)|(SpecialistFailurePolicyTests*)|(SpecialistQualificationContractTests*)|(SpecialistRoleContractTests*)|(StandingBootDocumentationTests*)|(StandingBootWatchPolicyTests*)|(StandingPipelinePolicyDocumentationTests*)|(StandingSpecialistHealthPolicyTests*)|(SubagentNotificationTests*)|(SubscriptionQuotaGateTests*)|(SubscriptionUsageParserTests*)/* executed=239 passed=239 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-64-20261030-202206-7b24/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67a commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=ok filter=/*/Antiphon.Tests.Checkpoints/(BaselineComparerTests*)|(BuildSlotClientTests*)|(C1307CliCancellationTests*)|(CheckpointAdmissionBudgetTests*)|(CheckpointAdmissionContractTests*)/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-67a/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67b commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointAppTests*)|(CheckpointBoundedReaderTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointConfinementLedgerTests*)|(CheckpointEofStallTests*)/* executed=48 passed=48 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-67b-20261030-192143-89f9/run.trx slot=granted waited=180s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67c commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointExecutorLogTests*)|(CheckpointFixtureProcessTests*)|(CheckpointFreshOutputSealTests*)|(CheckpointImportTests*)|(CheckpointLargeStampScriptTests*)/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-67c/run.trx slot=granted waited=240s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67d commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointLargeStampTests*)|(CheckpointLaunchCleanupTests*)|(CheckpointLineTests*)|(CheckpointManifestTests*)|(CheckpointNamespaceCensusUsageTests*)/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-67d/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67e commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointOrFilterTests*)|(CheckpointOutputConfinementTests*)|(CheckpointOwnedOutputTests*)|(CheckpointProcessIdentityTests*)|(CheckpointReceiptSafetyTests*)/* executed=61 passed=61 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-67e-20261030-193528-0275/run.trx slot=granted waited=225s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)|(CheckpointRepeatConfigurationTests*)|(CheckpointRepeatDocumentationTests*)|(CheckpointRepeatHostTests*)|(CheckpointRunOwnershipTests*)|(CheckpointScriptReaderDeadlineTests*)|(CheckpointShapeConflictTests*)|(CheckpointSlotContractTests*)|(CheckpointStampSafetyTests*)|(CheckpointTaskOwnershipTests*)|(CheckpointTempRootSweepTests*)|(CheckpointTempScopeTests*)|(CheckpointTimingHarnessTests*)|(CheckpointToolCopyCleanupTests*)|(CheckpointToolSourceTests*)|(CheckpointTrackedBaselineTests*)|(CheckpointWindowsReadModeTests*)|(DetachedLauncherTests*)|(EvidenceFolderTests*)|(ExitCodeTests*)/* executed=214 passed=214 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-68-20261030-194127-3ea4/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)|(PlanCoverageAssertionTests*)|(PlanCoverageBindingTests*)|(PlanCoverageCensusTests*)|(PlanCoverageCommandTests*)|(PlanCoverageGoldenTests*)|(PlanCoverageHandleTests*)|(PlanCoverageParserTests*)|(PlanCoveragePcTests*)|(ProcessDriverTests*)|(ReapedProcStatIdentityTests*)|(ReportMergerTests*)|(ReportWriterTests*)|(RerunFilterHostTests*)|(RerunPolicyTests*)|(RowRunnerTests*)|(RunSchedulerTests*)|(RunStateStoreTests*)|(SafeDeleteProcessBoundaryTests*)|(ShadowCopyTests*)|(TestPhaseCustodyTests*)|(TestSetTimingTests*)|(TimeoutTests*)|(TimingJournalTests*)|(TrxReportTests*)/* executed=188 passed=188 failed=0 skipped=8 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-69-20261030-202951-28a8/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-70 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/WaitCommandTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-70/run.trx slot=granted waited=60s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-71 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Domain.StateMachine/(CardStateMachineTests*)|(RunAttemptStateMachineTests*)|(WorkflowStateMachineTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-71/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-72 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(AppDbContextModelTests*)|(AppHostBrokerSourceGuardTests*)|(C1307CancellationTests*)|(C1307ToolRemovalTests*)|(CheckpointToolProjectContractTests*)|(CodexRootExecContractTests*)|(CodexRunnerImageContractTests*)|(CustodyHelperContractTests*)|(DindRunnerContractTests*)|(DirectoryBuildRspTests*)|(DockerDeliveryBarrierTests*)|(DockerDeliveryObservationTests*)|(DockerStackContractTests*)/* executed=246 passed=246 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-72/run.trx slot=granted waited=135s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-73 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(DockerStackDocumentationTests*)|(DotnetToolManifestContractTests*)|(E2ECopyOwnedPtyHostAppHostContractTests*)|(EvidencePolicyWorkflowTests*)|(FileSystemEvidenceRetentionTests*)|(FileUseWindowsTests*)|(GithubCredentialHelperTests*)|(GitMinimumVersionGuardTests*)|(GrokRunnerImageContractTests*)|(GrokSha256PinTests*)|(HostCleanupCheckpointAuthorityTests*)|(HostCleanupCheckpointCompatibilityTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointExtractionTests*)|(HostCleanupCheckpointPathTests*)|(HostCleanupCheckpointReadDeadlineTests*)|(HostCleanupCheckpointReceiptTests*)|(HostCleanupCheckpointSweepRaceTests*)|(HostCleanupExecutionTests*)|(HostCleanupOwnershipTests*)|(HostCleanupPolicyTests*)|(JqRunnerImageContractTests*)/* executed=248 passed=248 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-73/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-74 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)|(LandingIdentityControlTests*)|(LandingRemovalPolicyControlTests*)|(LogRetentionTests*)|(OperatorDashboardSessionsTests*)|(OperatorTokenFileTests*)|(OrchestratorInstructionsMigrationShapeTests*)|(PackageVersionPinContractTests*)|(PostgresPoolConfigurationTests*)|(RunnerPushCredentialDocsTests*)|(SessionRunnerGrokLineEndingContractTests*)|(ShellFixtureLineEndingContractTests*)|(WorktreeDeleteAccessProbeTests*)|(WorktreeLockDiagnosticsTests*)|(WorktreeManagerSafetyTests*)|(WorktreeManagerTests*)/* executed=248 passed=248 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-74-20261030-203457-3309/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-75 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/WorktreeRemovalDefaultTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-75/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-76 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure.Resilience/(HttpReadResilienceTests*)|(HttpResilienceRegistrationTests*)|(ResilienceBudgetTests*)|(ResilienceCircuitTests*)|(ResilienceOptionsAndTelemetryTests*)|(ResiliencePredicateTests*)/* executed=46 passed=46 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-76/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-77 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Migrations/(CommitOnSettleMigrationTests*)|(PipelineDefinitionMigrationShapeTests*)/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-77/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-78 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)|(C1364BoundLandWaitProofTests*)|(DelegateScriptInterruptSwitchTests*)|(DelegateScriptRunnerSwitchTests*)|(HostJqPrerequisiteScriptTests*)|(ProcessSpawnTimeoutPolicyTests*)|(QualificationCheckoutSourceTests*)|(RefreshGithubTokenRemoteCommandTests*)|(RefreshGithubTokenScriptTests*)/* executed=65 passed=65 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-78-20261030-195603-1559/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-80 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=75s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-81 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)|(CheckCompactionFixtureTests*)|(ControlledLandingGitTests*)|(DelegationHarnessCensusTests*)|(DelegationTestServicesTests*)|(DirectoryLinkFixtureTests*)|(EntityScalarSnapshotConsumerGuardTests*)|(EntityScalarSnapshotTests*)|(LiveFixtureTextTests*)|(LoadTimeoutBoundWitnessTests*)|(LoadTimeoutWaitTests*)|(PairedOverheadStatisticsTests*)|(ScaledTimeProviderTests*)|(SlowTestTripwireTests*)|(TestAppHostPathTests*)|(TestClassificationPolicyTests*)|(TestDbFixtureLifecycleTests*)|(TestDbFixtureSchemaCacheLifecycleTests*)|(TestLaneCategoryGuardTests*)|(TestPhaseSummaryTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-81-20261030-203603-3101/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-82 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.TestHelpers/(TestPhaseSupervisorTests*)|(TestSchemaCacheTests*)|(TestTimingLifecycleTests*)|(TestTimingRecorderTests*)|(WindowsDeliveryFixtureWitnessTests*)/* executed=37 passed=37 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-82-20261030-203428-ea28/run.trx slot=granted waited=120s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-83 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.TestSupport/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-83-20261030-203529-2260/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-84 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-84-20261030-203551-65b3/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-85 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionContinuationTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-85-20261030-200229-eb4b/run.trx slot=granted waited=195s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-86 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionCrashTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-86-20261030-200516-8516/run.trx slot=granted waited=135s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-87 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionAttentionTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-87-20261030-200934-3201/run.trx slot=granted waited=90s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-88 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionWorkerLifetimeTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-88-20261030-201033-447b/run.trx slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-89 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionReceiptTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-89-20261030-201236-9bff/run.trx slot=granted waited=45s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-90 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionAutomaticRestartTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-90-20261030-201802-7714/run.trx slot=granted waited=240s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-91 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/*/CheckCompactionRecoveryPersistenceTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-91-20261030-201851-883e/run.trx slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
```

### Earlier unsuccessful diagnostic receipts retained

```text
CHECKPOINT CP-67b commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointAppTests*)|(CheckpointBoundedReaderTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointConfinementLedgerTests*)|(CheckpointEofStallTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=30s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-67e commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointOrFilterTests*)|(CheckpointOutputConfinementTests*)|(CheckpointOwnedOutputTests*)|(CheckpointProcessIdentityTests*)|(CheckpointReceiptSafetyTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=105s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointReceiptStallBudgetTests*)|(CheckpointRepeatConfigurationTests*)|(CheckpointRepeatDocumentationTests*)|(CheckpointRepeatHostTests*)|(CheckpointRunOwnershipTests*)|(CheckpointScriptReaderDeadlineTests*)|(CheckpointShapeConflictTests*)|(CheckpointSlotContractTests*)|(CheckpointStampSafetyTests*)|(CheckpointTaskOwnershipTests*)|(CheckpointTempRootSweepTests*)|(CheckpointTempScopeTests*)|(CheckpointTimingHarnessTests*)|(CheckpointToolCopyCleanupTests*)|(CheckpointToolSourceTests*)|(CheckpointTrackedBaselineTests*)|(CheckpointWindowsReadModeTests*)|(DetachedLauncherTests*)|(EvidenceFolderTests*)|(ExitCodeTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=6m slot=granted waited=0s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)|(PlanCoverageAssertionTests*)|(PlanCoverageBindingTests*)|(PlanCoverageCensusTests*)|(PlanCoverageCommandTests*)|(PlanCoverageGoldenTests*)|(PlanCoverageHandleTests*)|(PlanCoverageParserTests*)|(PlanCoveragePcTests*)|(ProcessDriverTests*)|(ReapedProcStatIdentityTests*)|(ReportMergerTests*)|(ReportWriterTests*)|(RerunFilterHostTests*)|(RerunPolicyTests*)|(RowRunnerTests*)|(RunSchedulerTests*)|(RunStateStoreTests*)|(SafeDeleteProcessBoundaryTests*)|(ShadowCopyTests*)|(TestPhaseCustodyTests*)|(TestSetTimingTests*)|(TimeoutTests*)|(TimingJournalTests*)|(TrxReportTests*)/* executed=188 passed=188 failed=0 skipped=8 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-69/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-74 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Infrastructure/(LandingGitProfileTests*)|(LandingIdentityControlTests*)|(LandingRemovalPolicyControlTests*)|(LogRetentionTests*)|(OperatorDashboardSessionsTests*)|(OperatorTokenFileTests*)|(OrchestratorInstructionsMigrationShapeTests*)|(PackageVersionPinContractTests*)|(PostgresPoolConfigurationTests*)|(RunnerPushCredentialDocsTests*)|(SessionRunnerGrokLineEndingContractTests*)|(ShellFixtureLineEndingContractTests*)|(WorktreeDeleteAccessProbeTests*)|(WorktreeLockDiagnosticsTests*)|(WorktreeManagerSafetyTests*)|(WorktreeManagerTests*)/* executed=248 passed=248 failed=0 skipped=2 trx=/work/worktrees/task-698bf6b9/.antiphon/checkpoints/20261030-184126-5d9d/rows/CP-74/run.trx slot=granted waited=15s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-78 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(C1008PlatformContractTests*)|(C1364BoundLandWaitProofTests*)|(DelegateScriptInterruptSwitchTests*)|(DelegateScriptRunnerSwitchTests*)|(HostJqPrerequisiteScriptTests*)|(ProcessSpawnTimeoutPolicyTests*)|(QualificationCheckoutSourceTests*)|(RefreshGithubTokenRemoteCommandTests*)|(RefreshGithubTokenScriptTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=4m slot=granted waited=120s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-80 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=75s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
CHECKPOINT CP-81 commit=b4cea3d0e1d3f9e47af86095696a72a25bcd530a build=reused filter=/*/Antiphon.Tests.TestHelpers/(BuildOutputNestingTests*)|(CheckCompactionFixtureTests*)|(ControlledLandingGitTests*)|(DelegationHarnessCensusTests*)|(DelegationTestServicesTests*)|(DirectoryLinkFixtureTests*)|(EntityScalarSnapshotConsumerGuardTests*)|(EntityScalarSnapshotTests*)|(LiveFixtureTextTests*)|(LoadTimeoutBoundWitnessTests*)|(LoadTimeoutWaitTests*)|(PairedOverheadStatisticsTests*)|(ScaledTimeProviderTests*)|(SlowTestTripwireTests*)|(TestAppHostPathTests*)|(TestClassificationPolicyTests*)|(TestDbFixtureLifecycleTests*)|(TestDbFixtureSchemaCacheLifecycleTests*)|(TestLaneCategoryGuardTests*)|(TestPhaseSummaryTests*)/* executed=249 passed=249 failed=0 skipped=1 trx=/work/worktrees/task-698bf6b9/.antiphon/c1610-p2/rows/CP-81-20261030-202917-58f3/run.trx slot=granted waited=135s dirty=0 source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a sourceState=clean buildSource=verified
```

### Interrupted selection journals (no completed-count credit)

| Selection | Started / completed journal cases | Observed outcomes | Partial lifecycle p50 / p95 / max s | Last started identity |
|---|---|---|---|---|
| CP-79 | 62 / 61 | {"Passed":61} | 1.558 / 57.271 / 398.283 | Antiphon.Tests.Scripts.RemoteScriptContractTests.1.1.C1008_Recycle_receipt_records_disk_and_partial_failure.1.1.0 |
| CP-80-final | 27 / 26 | {"Passed":26} | 14.649 / 116.276 / 131.925 | Antiphon.Tests.Scripts.RetiredTempContainerScriptTests.1.1.C994_Verifier_manifest_is_strict.1.1.0 |

These partial quantiles are censored, not row p95 or valid completed-coverage receipts. CP-80 has a completed **timeout** report (exit5), wall979.915s / receipt admission75s / execution envelope904.915s, bound15min, no TRX. Its report-level verdict is INVALID with reason `source_ineligible`, even though the retained start/end snapshots themselves are clean and build-verified; the timeout is not source-qualified green. CP-79 has no final aggregate report.

### Report validation and duration advisories

- CP-67b: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-67e: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-68: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-78: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-85: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-86: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-87: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-88: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-89: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-90: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-91: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-46: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-52: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-54: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-55: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-56: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-62: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-64: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-69: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-74: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-80: exit2, `CHECKPOINT SOURCE INVALID reason=report_failed`.
- CP-81: exit2, `CHECKPOINT SOURCE INVALID reason=row_failed`.
- CP-82: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-83: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.
- CP-84: exit0, `CHECKPOINT SOURCE VALID source=b4cea3d0e1d3f9e47af86095696a72a25bcd530a rows=1`.

The unmodified duration tripwire covered 34 completed TRX selections: 18 selections contain 175 unlisted >=5s entries; 0 invalid inputs. These are performance advisories, not assertion failures. Preserve the incomplete gate; do not add blanket Slow tags, change the registry or rerun Review solely for advisories (D-2). Heavy fixture work belongs in the measured follow-up. All original logs remain ignored.

```text
CP-67b
SLOW-TEST TRIPWIRE: expanded rows=48
SLOW-TEST TRIPWIRE: 16 unlisted tests >= 5s
8.543s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger  tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger
31.666s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(tool-git, held)
121.049s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(tool-eval, held)
121.245s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(script-eval, held)
6.080s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered  ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered
7.351s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.changed_file_under_an_output_root_is_still_refused_inside_the_slot  changed_file_under_an_output_root_is_still_refused_inside_the_slot
15.888s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes  changed_readers_bound_fifo_and_exited_child_pipes(script-git, held)
6.526s  Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.C1381_Project_read_budget_excludes_startup_delay  C1381_Project_read_budget_excludes_startup_delay
5.567s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.scheduler_reports_an_unreadable_ledger_entry_as_unreadable  scheduler_reports_an_unreadable_ledger_entry_as_unreadable
12.403s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.strict_admission_hash_in_the_manifest_refuses_the_build_before_launch  strict_admission_hash_in_the_manifest_refuses_the_build_before_launch
6.351s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode  shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode
5.425s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch  rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch
7.869s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.stat_without_a_change_time_hashes_each_entry_at_admission  stat_without_a_change_time_hashes_each_entry_at_admission
6.578s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted  entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted
5.482s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it  ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it
6.105s  Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch  portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch
CP-67c
SLOW-TEST TRIPWIRE: expanded rows=40
SLOW-TEST TRIPWIRE: 5 unlisted tests >= 5s
8.739s  Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.C1573_linux_only_cases_skip_when_stat_is_absent  C1573_linux_only_cases_skip_when_stat_is_absent
5.159s  Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse  C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse
13.844s  Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.C1532_script_stamp_matches_full_digest  C1532_script_stamp_matches_full_digest
22.775s  Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.C1532_script_digest_limits_stalls_and_eof  C1532_script_digest_limits_stalls_and_eof
10.951s  Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.C1532_scripts_are_ascii_and_parse_in_pwsh  C1532_scripts_are_ascii_and_parse_in_pwsh
CP-67d
SLOW-TEST TRIPWIRE: expanded rows=28
SLOW-TEST TRIPWIRE: 5 unlisted tests >= 5s
20.627s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_content_and_receipt_limits_are_distinct  C1532_content_and_receipt_limits_are_distinct
6.764s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_tail_edit_with_restored_mtime_is_tampered  C1532_tail_edit_with_restored_mtime_is_tampered
5.992s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_large_ignored_paths_remain_covered  C1532_large_ignored_paths_remain_covered
23.310s  Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.C1532_second_build_and_retry_admit_large_prior_output  C1532_second_build_and_retry_admit_large_prior_output
5.566s  Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases  namespace_census_matches_compiled_checkpoint_cases
CP-67e
SLOW-TEST TRIPWIRE: expanded rows=62
SLOW-TEST TRIPWIRE: 13 unlisted tests >= 5s
13.698s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build  unevaluable_import_is_refused_before_the_build
39.748s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency  transitive_import_build_preserves_the_tracked_dependency
29.703s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency  conditional_import_build_preserves_the_tracked_dependency
27.864s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency  imported_props_build_preserves_the_tracked_dependency
26.416s  Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency  directory_build_props_build_preserves_the_tracked_dependency
6.073s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.linux_fifo_receipts_refuse_within_four_seconds  linux_fifo_receipts_refuse_within_four_seconds
7.904s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.symlink_and_directory_receipts_refuse_within_four_seconds  symlink_and_directory_receipts_refuse_within_four_seconds
15.874s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.malformed_collection_and_string_shapes_return_named_refusals  malformed_collection_and_string_shapes_return_named_refusals
17.066s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing()
22.130s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing( \t
19.313s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing(null)
25.285s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing  receipt_row_and_build_identities_refuse_blank_without_writing(CP-fixture)
50.539s  Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.malformed_state_shape_by_verb_table_matches_documented_exits  malformed_state_shape_by_verb_table_matches_documented_exits
CP-68
SLOW-TEST TRIPWIRE: expanded rows=215
SLOW-TEST TRIPWIRE: 34 unlisted tests >= 5s
5.041s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.windows_estimate_selects_row_and_total_deadlines  windows_estimate_selects_row_and_total_deadlines
5.060s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.settlement_cancels_two_drivers_and_skips_later_rows  settlement_cancels_two_drivers_and_skips_later_rows
5.858s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.unbound_runs_and_normal_starter_exit_remain_allowed  unbound_runs_and_normal_starter_exit_remain_allowed
6.001s  Antiphon.Tests.Checkpoints.CheckpointTempRootSweepTests.disposed_roots_do_not_consume_the_next_sweep_budget  disposed_roots_do_not_consume_the_next_sweep_budget
10.142s  Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests.canceled_process_does_not_wait_forever_for_inherited_output_pipes  canceled_process_does_not_wait_forever_for_inherited_output_pipes
19.824s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(staged-untracked)
19.741s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(file-on-directory)
18.323s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(directory-on-file)
19.475s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(ignored-descendant)
7.367s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(existing-output-symlink)
7.080s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(compiler-collision)
18.776s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(symlink-on-file)
9.927s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(existing-output-directory)
33.856s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(changed-untracked)
19.806s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(regular-user-move)
16.231s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(deleted-untracked)
50.089s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(clean-build)
18.852s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(deleted-tracked)
18.750s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(modified-tracked)
7.062s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(existing-stamp)
19.658s  Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_conflict_rule  shape_conflict_rule(untracked-descendant)
22.812s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.intermediate_output_path_tampering_is_reported  intermediate_output_path_tampering_is_reported
19.767s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.generated_target_file_tampering_is_reported  generated_target_file_tampering_is_reported
25.093s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported  imported_props_reference_tampering_is_reported
5.399s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.dirty_tracked_tree_is_refused_and_unmodified  dirty_tracked_tree_is_refused_and_unmodified
20.913s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero  clean_untracked_output_build_exits_zero
38.852s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test  fresh_checkout_run_executes_a_test
6.219s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files  tracked_snapshot_contains_committed_files
19.809s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed  staged_untracked_file_is_kept_and_indexed
37.048s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed  staged_ignored_file_is_kept_and_indexed
23.800s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported  local_output_property_tampering_is_reported
29.795s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported  transitive_reference_tampering_is_reported
13.468s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt  passing_script_requires_writable_source_receipt
16.834s  Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.index_added_file_is_kept_and_indexed  index_added_file_is_kept_and_indexed
CP-72
SLOW-TEST TRIPWIRE: expanded rows=246
SLOW-TEST TRIPWIRE: 4 unlisted tests >= 5s
7.093s  Antiphon.Tests.Infrastructure.AppDbContextModelTests.AgentTasks_has_project_id_status_index  AgentTasks_has_project_id_status_index
7.096s  Antiphon.Tests.Infrastructure.AppDbContextModelTests.Model_sql_fragments_have_no_carriage_returns  Model_sql_fragments_have_no_carriage_returns
9.645s  Antiphon.Tests.Infrastructure.AppDbContextModelTests.Current_model_matches_migration_snapshot  Current_model_matches_migration_snapshot
15.728s  Antiphon.Tests.Infrastructure.CodexRootExecContractTests.Provider_exec_into_the_runner_names_uid_1654  Provider_exec_into_the_runner_names_uid_1654
CP-73
SLOW-TEST TRIPWIRE: expanded rows=248
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
6.223s  Antiphon.Tests.Infrastructure.HostCleanupCheckpointCompatibilityTests.disposed_roots_do_not_consume_the_next_sweep_budget  disposed_roots_do_not_consume_the_next_sweep_budget
7.389s  Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool  Fresh_context_retains_unproven_tool(malformed)
CP-76
SLOW-TEST TRIPWIRE: expanded rows=46
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
7.677s  Antiphon.Tests.Infrastructure.Resilience.ResilienceCircuitTests.Zero_queue_limiter_rejects_without_retry_and_database_scope_is_disposed_before_delay  Zero_queue_limiter_rejects_without_retry_and_database_scope_is_disposed_before_delay
CP-78
SLOW-TEST TRIPWIRE: expanded rows=67
SLOW-TEST TRIPWIRE: 20 unlisted tests >= 5s
5.693s  Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests.Spawning_classes_carry_a_class_timeout  Spawning_classes_carry_a_class_timeout
7.257s  Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests.Heredoc_fed_docker_exec_keeps_stdin_open  Heredoc_fed_docker_exec_keeps_stdin_open
10.158s  Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests.Spawn_timeouts_do_not_exceed_ten_minutes  Spawn_timeouts_do_not_exceed_ten_minutes
16.490s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Wrapper_cleanup_reports_waited  C1364_Wrapper_cleanup_reports_waited
7.107s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_publishes_complete_no_clobber  C1025_Provision_publishes_complete_no_clobber
128.626s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_admission_reports_waited  C1364_Host_recycle_admission_reports_waited
142.368s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited  C1364_Host_recycle_post_removal_reports_waited
15.079s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Wrapper_redeploy_reports_waited  C1364_Wrapper_redeploy_reports_waited
23.252s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Wrapper_retire_volumes_reports_waited  C1364_Wrapper_retire_volumes_reports_waited
26.974s  Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests.C1364_Host_cleanup_reports_waited  C1364_Host_cleanup_reports_waited
64.145s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_is_fresh_for_each_entry  C1025_Preflight_is_fresh_for_each_entry
7.856s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Transport_is_bounded_and_reaped  C1025_Transport_is_bounded_and_reaped
119.605s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_requires_complete_current_proof  C1025_Receipt_requires_complete_current_proof
18.622s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Receipt_persistence_is_required  C1025_Receipt_persistence_is_required
6.170s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Explicit_tool_phases_never_deploy  C1025_Explicit_tool_phases_never_deploy
108.491s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Preflight_precedes_every_phase  C1025_Preflight_precedes_every_phase
7.927s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Final_recycle_guard_remains  C1025_Final_recycle_guard_remains
8.317s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1025_Provision_requires_canonical_source  C1025_Provision_requires_canonical_source
6.926s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Changed_leaf_cannot_emit_success_proof  C1058_Changed_leaf_cannot_emit_success_proof
9.590s  Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests.C1058_Metadata_syntax_refuses  C1058_Metadata_syntax_refuses
CP-85
SLOW-TEST TRIPWIRE: expanded rows=2
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
5.841s  Antiphon.Tests.Application.CheckCompactionContinuationTests.Successful_unchanged_pull_confirms_but_failed_pull_does_not  Successful_unchanged_pull_confirms_but_failed_pull_does_not
5.827s  Antiphon.Tests.Application.CheckCompactionContinuationTests.Confirmed_episode_survives_restart_and_incident_pruning  Confirmed_episode_survives_restart_and_incident_pruning
CP-87
SLOW-TEST TRIPWIRE: expanded rows=1
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
5.077s  Antiphon.Tests.Application.CheckCompactionAttentionTests.Open_episode_is_one_attention_row_until_it_is_recovered  Open_episode_is_one_attention_row_until_it_is_recovered
CP-89
SLOW-TEST TRIPWIRE: expanded rows=27
SLOW-TEST TRIPWIRE: 27 unlisted tests >= 5s
32.667s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Another_message_for_the_checked_task_is_not_the_receipt  Another_message_for_the_checked_task_is_not_the_receipt
32.654s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Caller_prompt_at_or_before_baseline_does_not_recover  Caller_prompt_at_or_before_baseline_does_not_recover(older)
32.564s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Partial_caller_prompt_does_not_recover  Partial_caller_prompt_does_not_recover
32.686s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Caller_prompt_at_or_before_baseline_does_not_recover  Caller_prompt_at_or_before_baseline_does_not_recover(at-floor)
35.200s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Nonwinning_interpretation_cannot_claim_request_receipt  Nonwinning_interpretation_cannot_claim_request_receipt
35.192s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Prior_generation_request_cannot_recover_current_episode  Prior_generation_request_cannot_recover_current_episode
35.811s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt  Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt(suppressed)
35.847s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt  Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt(wrong-episode)
37.014s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(missing-interpreter-prompt)
37.051s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(not-sent)
37.052s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(submit-retry)
37.095s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_recovers_without_interpretation_parent  Modern_receipt_recovers_without_interpretation_parent(idle)
37.096s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-source)
37.007s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(sent-without-user-prompt)
37.098s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(absent-correlation)
37.013s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_recovers_without_interpretation_parent  Modern_receipt_recovers_without_interpretation_parent(busy)
37.107s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-caller)
37.109s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(absent-message)
37.110s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(receipt-before-sweep)
37.111s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-purpose)
37.111s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(absent-baseline)
37.111s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(queued-before-flush)
37.113s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(empty-result)
37.120s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Historical_legacy_publication_does_not_block_modern_receipt  Historical_legacy_publication_does_not_block_modern_receipt
37.121s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(wrong-origin)
37.237s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_requires_complete_evidence  Modern_receipt_requires_complete_evidence(missing-interpreter-end)
38.169s  Antiphon.Tests.Application.CheckCompactionReceiptTests.Modern_receipt_survives_publication_and_delivery_recreation  Modern_receipt_survives_publication_and_delivery_recreation(insert-failure)
CP-90
SLOW-TEST TRIPWIRE: expanded rows=11
SLOW-TEST TRIPWIRE: 11 unlisted tests >= 5s
11.713s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Crossed_stop_generation_cannot_confirm_exit  Crossed_stop_generation_cannot_confirm_exit
11.698s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Crossed_stop_attempt_cannot_confirm_exit  Crossed_stop_attempt_cannot_confirm_exit
11.880s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Unproven_standing_owner_never_restarts  Unproven_standing_owner_never_restarts
11.916s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Quota_hold_vetoes_stop  Quota_hold_vetoes_stop
12.163s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Authentication_refusal_vetoes_stop  Authentication_refusal_vetoes_stop
12.145s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Human_turn_vetoes_automatic_restart  Human_turn_vetoes_automatic_restart
12.151s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Model_hold_vetoes_stop  Model_hold_vetoes_stop
12.214s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Unsupported_stop_never_falls_back_or_resumes  Unsupported_stop_never_falls_back_or_resumes
12.212s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Provider_hold_vetoes_stop  Provider_hold_vetoes_stop
12.304s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.Capacity_hold_vetoes_stop  Capacity_hold_vetoes_stop
12.532s  Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests.One_stop_is_reconciled_without_a_second_launch  One_stop_is_reconciled_without_a_second_launch
CP-46
SLOW-TEST TRIPWIRE: expanded rows=216
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
7.312s  Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution  Disabling_the_probe_does_not_disable_effort_resolution(True)
CP-69
SLOW-TEST TRIPWIRE: expanded rows=196
SLOW-TEST TRIPWIRE: 21 unlisted tests >= 5s
6.475s  Antiphon.Tests.Checkpoints.OutputCleanupTests.deletes_only_this_runs_bin_names  deletes_only_this_runs_bin_names
7.255s  Antiphon.Tests.Checkpoints.OutputCleanupTests.never_deletes_bin_obj_workspace_or_foreign_bin_star  never_deletes_bin_obj_workspace_or_foreign_bin_star
7.349s  Antiphon.Tests.Checkpoints.OutputCleanupTests.red_keeps_unless_clean_on_red  red_keeps_unless_clean_on_red
8.099s  Antiphon.Tests.Checkpoints.OutputCleanupTests.wrapper_refuses_a_tracked_file_under_a_csproj_output_directory  wrapper_refuses_a_tracked_file_under_a_csproj_output_directory
8.937s  Antiphon.Tests.Checkpoints.OutputCleanupTests.evidence_under_antiphon_survives_and_dry_run_says_would_delete  evidence_under_antiphon_survives_and_dry_run_says_would_delete
9.001s  Antiphon.Tests.Checkpoints.PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery  census_reports_unmapped_selection_without_dynamic_discovery
5.070s  Antiphon.Tests.Checkpoints.RerunFilterHostTests.parenthesized_class_filter_runs_both_methods_on_a_real_host  parenthesized_class_filter_runs_both_methods_on_a_real_host
13.183s  Antiphon.Tests.Checkpoints.OutputCleanupTests.build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green  build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green
11.057s  Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5  c866_legacy_reports_v1_v4_v5
15.498s  Antiphon.Tests.Checkpoints.OutputCleanupTests.hanging_git_is_refused_and_killed_within_the_bound  hanging_git_is_refused_and_killed_within_the_bound
13.674s  Antiphon.Tests.Checkpoints.RunStateStoreTests.readers_never_see_a_partial_file  readers_never_see_a_partial_file
24.351s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.extra_frozen_method_is_a_checklist_count_finding  extra_frozen_method_is_a_checklist_count_finding
24.350s  Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible  c835_eight_pc_risks_are_visible
24.610s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.dropped_frozen_method_is_a_checklist_count_finding  dropped_frozen_method_is_a_checklist_count_finding
24.836s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.class_only_frozen_result_count_has_no_findings  class_only_frozen_result_count_has_no_findings
25.256s  Antiphon.Tests.Checkpoints.PlanCoverageCommandTests.frozen_checklist_preserves_all_72_obligations  frozen_checklist_preserves_all_72_obligations
27.336s  Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations  c866_fixed_checks_clear_same_obligations
27.169s  Antiphon.Tests.Checkpoints.PlanCoveragePcTests.c983_driver_aliases_bind_on_the_exit_assertion  c983_driver_aliases_bind_on_the_exit_assertion
57.613s  Antiphon.Tests.Checkpoints.OutputCleanupTests.adopted_build_output_over_the_entry_cap_is_unsafe_tree  adopted_build_output_over_the_entry_cap_is_unsafe_tree
152.327s  Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_row_timeout_checklist_binds_the_advisory_method  historical_row_timeout_checklist_binds_the_advisory_method
267.716s  Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_plans_bind_without_suppression  historical_plans_bind_without_suppression
CP-74
SLOW-TEST TRIPWIRE: expanded rows=250
SLOW-TEST TRIPWIRE: 1 unlisted tests >= 5s
19.086s  Antiphon.Tests.Infrastructure.OrchestratorInstructionsMigrationShapeTests.State_table_and_session_column_match_the_snapshot  State_table_and_session_column_match_the_snapshot
CP-81
SLOW-TEST TRIPWIRE: expanded rows=250
SLOW-TEST TRIPWIRE: 9 unlisted tests >= 5s
5.126s  Antiphon.Tests.TestHelpers.EntityScalarSnapshotTests.Snapshot_covers_exactly_the_reflected_non_navigation_properties  Snapshot_covers_exactly_the_reflected_non_navigation_properties(Antiphon.Server.Domain.Entities.AgentSession)
5.124s  Antiphon.Tests.TestHelpers.EntityScalarSnapshotTests.Snapshot_covers_exactly_the_reflected_non_navigation_properties  Snapshot_covers_exactly_the_reflected_non_navigation_properties(Antiphon.Server.Domain.Entities.AgentTask)
8.621s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G069  C487_G069(optin)
8.440s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G069  C487_G069(ordinary)
10.064s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G067  C487_G067(no-inherits)
12.154s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G067  C487_G067(inherits)
8.770s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G066  C487_G066
8.305s  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G065  C487_G065
6.347s  Antiphon.Tests.TestHelpers.TestPhaseSummaryTests.C1514T_MergeAndQuality  C1514T_MergeAndQuality(full-match)
CP-82
SLOW-TEST TRIPWIRE: expanded rows=37
SLOW-TEST TRIPWIRE: 2 unlisted tests >= 5s
9.107s  Antiphon.Tests.TestHelpers.TestPhaseSupervisorTests.C1514T_PlannedManifestAndEnvironment  C1514T_PlannedManifestAndEnvironment
6.027s  Antiphon.Tests.TestHelpers.TestPhaseSupervisorTests.C1514T_ActualAndFailurePaths  C1514T_ActualAndFailurePaths
```

### Exact incomplete selections for the next Code/Debug task

**CP-79, inherited floor 499, incomplete**

```text
/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/*
```

**CP-80, inherited floor 168, incomplete**

```text
/*/Antiphon.Tests.Scripts/(RetiredTempContainerHostTests*)|(RetiredTempContainerScriptTests*)|(RollingProductionMountTests*)|(RollingVolumeRecycleCensusChurnTests*)|(RollingVolumeRecycleScriptTests*)|(ScriptHarnessProcessContractTests*)|(SeedSmokeTargetingPackContractTests*)|(SelectorLineEndingAttributeTests*)|(TestTimingReportTests*)|(VerifyDevStackSmokeTests*)|(WindowsUnitPlatformAdmissionTests*)/*
```

Do not rerun either unchanged whole filter until the repeated fixture cost is addressed or an exact, count-preserving method partition plan is reviewed. After the repair commit, build once to a new literal isolated output under the host build-slot gate, import the revised plan and derive its finite execution/admission/wall bounds as documented. Execute its exact repaired partitions through `run`, then `wait` until the exit is not75; keep source frozen, retain all receipts and preserve the original failed/interrupted histories. Native Windows witnesses and the planned-skip report-validator contract require separate scoped work; this Linux task grants neither native qualification nor a validator waiver. No master assertion-red claim was made; prove any future such claim by the same failing method on a scratch origin/master.

### Verification closure before publication

Final plan import accepted 97 executable rows under lease 49e7e931-f5f6-4828-af9d-dc27517b58fb (60s admission /1s held). The pre-existing CP-27/28 default import 75min advisories were preserved; this task did not run those selections. All checkpoint table cells/filters match the imported candidate byte-for-byte; subsequent edits correct prose spacing only. The final executable list replaces one CP-67 aggregate with its five disjoint partitions. All original filters are retained, including the aggregate in history.

The final Linux skip count is 43: original 28 plus 15 new (CP-67e:1, CP-68:1, CP-69:8, CP-74:2, CP-78:2, CP-81:1). CP-81’s additional guard is actual Windows PowerShell 5.1/nightly custody at TestPhaseSummaryTests.cs:98; no Linux-native case was incorrectly skipped. Corrected-floor reruns CP-69/74/81 all exited 0; their earlier roster misses and their costs remain retained.

No production/test repairs were necessary or made. No new assertion-red was found, and no inherited-master-red assertion was claimed. The unresolved defects are scheduling/setup cost in CP-79/80 and the strict report-validator planned-skip contract, not unproven product regressions. Caller authority already covered the Debug and measured reruns; no extra approval was requested.

### CP-67 per-test phase ledger

Joined using full native `ANTIPHON_TEST_PHASE_ID` only (argument ordinal retained), never TRX GUID, display-name guessing or timestamps. Journal elapsed includes test lifecycle; TRX duration is separately measured. Empty phase spans mean **unattributed**, not zero external work. Every row is a single current sample; per-identity p95 cannot be estimated.

| Partition | Exact native identity / skipped case | Outcome | TRX s | Lifecycle s | Named phase spans / remainder s |
|---|---|---|---:|---:|---|
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.release_on_dispose.1.1.0 | Passed | 0.084 | 0.107 | No named spans; unattributed 0.107 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.baseline_stops_when_the_timeout_is_already_spent.1.1.0 | Passed | 0.133 | 0.196 | No named spans; unattributed 0.196 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.pre_existing_checkout_survives_when_creation_fails.1.1.0 | Passed | 0.136 | 0.196 | No named spans; unattributed 0.196 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.checkout_path_refuses_unsafe_build_ids.1.1.0 | Passed | 0.116 | 0.196 | No named spans; unattributed 0.196 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.removes_worktree_when_build_fails.1.1.0 | Passed | 0.173 | 0.198 | No named spans; unattributed 0.198 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_cancellation_joins_and_keeps_partial_wait.1.1.0 | Passed | 0.248 | 0.271 | No named spans; unattributed 0.271 |
| CP-67a | Antiphon.Tests.Checkpoints.C1307CliCancellationTests.1.1.Direct_row_cancel_reaches_slot_and_driver.1.1.0 | Passed | 0.253 | 0.285 | No named spans; unattributed 0.285 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_running_sibling_keeps_charging.1.1.0 | Passed | 0.264 | 0.290 | No named spans; unattributed 0.290 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_execution_overrun_keeps_old_limits.1.1.0 | Passed | 0.267 | 0.291 | No named spans; unattributed 0.291 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_stuck_admission_is_bounded.1.1.0 | Passed | 0.271 | 0.296 | No named spans; unattributed 0.296 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_wait_preserves_execution_budget.1.1.0 | Passed | 0.273 | 0.296 | No named spans; unattributed 0.296 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_overlapping_waits_receive_union_credit.1.1.0 | Passed | 0.273 | 0.298 | No named spans; unattributed 0.298 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.renewable_grant_is_renewed_until_the_checkpoint_releases_it(System.Threading.CancellationToken).1.1.0 | Passed | 0.100 | 0.100 | No named spans; unattributed 0.100 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders.1.1.0 | Passed | 0.277 | 0.302 | No named spans; unattributed 0.302 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.null_renewal_interval_is_granted_without_renewal.1.1.0 | Passed | 0.023 | 0.023 | No named spans; unattributed 0.023 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.baseline_checkout_is_outside_the_worktree_and_evidence_stays_in_the_run.1.1.0 | Passed | 0.302 | 0.327 | No named spans; unattributed 0.327 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.adds_and_removes_the_detached_worktree_once_per_build.1.1.0 | Passed | 0.303 | 0.328 | No named spans; unattributed 0.328 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.timeout_is_exit_4.1.1.0 | Passed | 0.007 | 0.006 | No named spans; unattributed 0.006 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_hard_wall_bounds_successive_waits.1.1.0 | Passed | 0.311 | 0.335 | No named spans; unattributed 0.335 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.pid_liveness_grant_with_null_renewal_is_granted_without_renewing.1.1.0 | Passed | 0.016 | 0.016 | No named spans; unattributed 0.016 |
| CP-67a | Antiphon.Tests.Checkpoints.BaselineComparerTests.1.1.classifies_inherited_introduced_new.1.1.0 | Passed | 0.303 | 0.330 | No named spans; unattributed 0.330 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.pid_idempotent_broker_grants_distinct_leases_and_one_release_keeps_the_other.1.1.0 | Passed | 0.010 | 0.010 | No named spans; unattributed 0.010 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_ready_row_waits_then_executes.1.1.0 | Passed | 0.378 | 0.403 | No named spans; unattributed 0.403 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionBudgetTests.1.1.C1531_baseline_shares_remaining_budget.1.1.0 | Passed | 0.398 | 0.424 | No named spans; unattributed 0.424 |
| CP-67a | Antiphon.Tests.Checkpoints.C1307CliCancellationTests.1.1.Main_interrupt_reaches_command_and_unsubscribes.1.1.0 | Passed | 0.403 | 0.427 | No named spans; unattributed 0.427 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.holder_process_stays_alive_until_the_lease_is_released.1.1.0 | Passed | 0.145 | 0.146 | No named spans; unattributed 0.146 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.a_shared_pid_lease_is_not_claimed_by_the_second_driver.1.1.0 | Passed | 0.003 | 0.003 | No named spans; unattributed 0.003 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.refused_becomes_unleased_after_grace.1.1.0 | Passed | 0.008 | 0.008 | No named spans; unattributed 0.008 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.probe_404_marks_unavailable_and_makes_no_lease_calls.1.1.0 | Passed | 0.002 | 0.002 | No named spans; unattributed 0.002 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.memory_floor_waits.1.1.0 | Passed | 0.001 | 0.001 | No named spans; unattributed 0.001 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.renewal_delay_is_separate_from_acquisition_delay(System.Threading.CancellationToken).1.1.0 | Passed | 0.005 | 0.005 | No named spans; unattributed 0.005 |
| CP-67a | Antiphon.Tests.Checkpoints.BuildSlotClientTests.1.1.busy_waits_then_grants.1.1.0 | Passed | 0.003 | 0.003 | No named spans; unattributed 0.003 |
| CP-67a | Antiphon.Tests.Checkpoints.C1307CliCancellationTests.1.1.Cancel_before_request_read_is_not_crash.1.1.0 | Passed | 0.804 | 0.858 | No named spans; unattributed 0.858 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests.1.1.C1531_inputs_resolve_without_disabling_bounds.1.1.0 | Passed | 0.692 | 0.698 | No named spans; unattributed 0.698 |
| CP-67a | Antiphon.Tests.Checkpoints.C1307CliCancellationTests.1.1.Executor_cancel_persists_130.1.1.0 | Passed | 2.328 | 2.374 | No named spans; unattributed 2.374 |
| CP-67a | Antiphon.Tests.Checkpoints.C1307CliCancellationTests.1.1.No_caller_cancel_preserves_existing_results.1.1.0 | Passed | 2.336 | 2.395 | No named spans; unattributed 2.395 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests.1.1.C1531_receipts_keep_wait_execution_and_counts.1.1.0 | Passed | 1.374 | 1.394 | No named spans; unattributed 1.394 |
| CP-67a | Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests.1.1.C1531_legacy_receipts_remain_unknown_not_zero.1.1.0 | Passed | 0.053 | 0.053 | No named spans; unattributed 0.053 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointAppTests.1.1.phase_done_is_published_before_cleanup_and_cleanup_failures_are_kept.1.1.0 | Passed | 0.155 | 0.287 | No named spans; unattributed 0.287 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.stamp_catch_formats_the_innermost_reason.1.1.0 | Passed | 0.490 | 0.535 | No named spans; unattributed 0.535 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.fixture_text_is_normalized_before_git_can_stage_it.1.1.0 | Passed | 0.066 | 0.291 | No named spans; unattributed 0.291 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.eof_stall_without_growth_still_completes(System.String).3.1.0 | Passed | 0.816 | 0.857 | No named spans; unattributed 0.857 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.eof_stall_without_growth_still_completes(System.String).2.1.0 | Passed | 0.823 | 0.863 | No named spans; unattributed 0.863 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.append_during_eof_stall_is_refused(System.String).1.1.0 | Passed | 0.825 | 0.875 | No named spans; unattributed 0.875 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.append_during_eof_stall_is_refused(System.String).3.1.0 | Passed | 0.825 | 0.865 | No named spans; unattributed 0.865 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.append_during_eof_stall_is_refused(System.String).2.1.0 | Passed | 0.825 | 0.866 | No named spans; unattributed 0.866 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.eof_stall_with_unprovable_final_size_is_refused(System.String).1.1.0 | Passed | 0.859 | 0.894 | No named spans; unattributed 0.894 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.1.1.Producer_stamp_is_accepted_by_reconstructed_audit_path.1.1.0 | Passed | 0.889 | 0.895 | No named spans; unattributed 0.895 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.eof_stall_with_unprovable_final_size_is_refused(System.String).2.1.0 | Passed | 0.859 | 0.900 | No named spans; unattributed 0.900 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.eof_stall_without_growth_still_completes(System.String).1.1.0 | Passed | 0.865 | 0.905 | No named spans; unattributed 0.905 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointEofStallTests.1.1.eof_stall_with_unprovable_final_size_is_refused(System.String).3.1.0 | Passed | 0.868 | 0.908 | No named spans; unattributed 0.908 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.1.1.Different_directory_or_provenance_is_refused.1.1.0 | Passed | 1.050 | 1.055 | No named spans; unattributed 1.055 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.1.1.Equivalent_directory_spellings_preserve_binding.1.1.0 | Passed | 1.102 | 1.131 | No named spans; unattributed 1.131 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger(System.Threading.CancellationToken).1.1.0 | Passed | 8.543 | 8.752 | No named spans; unattributed 8.752 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).4.1.0 | Passed | 31.666 | 31.670 | No named spans; unattributed 31.670 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).7.1.0 | Passed | 1.913 | 1.914 | No named spans; unattributed 1.914 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointAppTests.1.1.create_run_uses_the_manifest_results_root.1.1.0 | Passed | 0.207 | 0.214 | No named spans; unattributed 0.214 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).8.1.0 | Passed | 121.049 | 121.050 | No named spans; unattributed 121.050 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).2.1.0 | Passed | 1.382 | 1.383 | No named spans; unattributed 1.383 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).10.1.0 | Passed | 121.245 | 121.246 | No named spans; unattributed 121.246 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).3.1.0 | Passed | 1.106 | 1.107 | No named spans; unattributed 1.107 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered.1.1.0 | Passed | 6.080 | 6.141 | No named spans; unattributed 6.141 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.changed_file_under_an_output_root_is_still_refused_inside_the_slot(System.Threading.CancellationToken).1.1.0 | Passed | 7.351 | 7.496 | No named spans; unattributed 7.496 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).5.1.0 | Passed | 0.964 | 0.965 | No named spans; unattributed 0.965 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointAppTests.1.1.create_run_preserves_finished_and_live_sibling_tool_copies.1.1.0 | Passed | 0.290 | 0.306 | No named spans; unattributed 0.306 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).6.1.0 | Passed | 15.888 | 15.889 | No named spans; unattributed 15.889 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.admission_hash_other_than_stat_or_strict_is_rejected.1.1.0 | Passed | 0.042 | 0.043 | No named spans; unattributed 0.043 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).1.1.0 | Passed | 1.392 | 1.393 | No named spans; unattributed 1.393 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.changed_readers_bound_fifo_and_exited_child_pipes(System.String, System.String).9.1.0 | Passed | 1.120 | 1.121 | No named spans; unattributed 1.121 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.1.1.C1381_Project_read_budget_excludes_startup_delay.1.1.0 | Passed | 6.526 | 6.526 | No named spans; unattributed 6.526 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.scheduler_reports_an_unreadable_ledger_entry_as_unreadable(System.Threading.CancellationToken).1.1.0 | Passed | 5.567 | 5.704 | No named spans; unattributed 5.704 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.tampered_entry_beside_an_unreadable_entry_is_still_refused_tampered.1.1.0 | Passed | 3.125 | 3.261 | No named spans; unattributed 3.261 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.strict_admission_hash_in_the_manifest_refuses_the_build_before_launch(System.Threading.CancellationToken).1.1.0 | Passed | 12.403 | 12.590 | No named spans; unattributed 12.590 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode.1.1.0 | Passed | 6.351 | 6.434 | No named spans; unattributed 6.434 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch(System.Threading.CancellationToken).1.1.0 | Passed | 5.425 | 5.610 | No named spans; unattributed 5.610 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission.1.1.0 | Passed | 4.525 | 4.594 | No named spans; unattributed 4.594 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.untracked_source_file_added_during_the_build_is_still_refused(System.Threading.CancellationToken).1.1.0 | Passed | 2.114 | 2.226 | No named spans; unattributed 2.226 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.stat_without_a_change_time_hashes_each_entry_at_admission.1.1.0 | Passed | 7.869 | 8.052 | No named spans; unattributed 8.052 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable(System.Threading.CancellationToken).1.1.0 | Passed | 2.568 | 2.684 | No named spans; unattributed 2.684 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.non_output_file_changed_before_slot_release_is_refused.1.1.0 | Passed | 3.049 | 3.113 | No named spans; unattributed 3.113 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release(System.Threading.CancellationToken).1.1.0 | Passed | 4.985 | 5.139 | No named spans; unattributed 5.139 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted.1.1.0 | Passed | 6.578 | 6.689 | No named spans; unattributed 6.689 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted.1.1.0 | Passed | 4.103 | 4.200 | No named spans; unattributed 4.200 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it(System.Threading.CancellationToken).1.1.0 | Passed | 5.482 | 5.552 | No named spans; unattributed 5.552 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch(System.Threading.CancellationToken).1.1.0 | Passed | 6.105 | 6.257 | No named spans; unattributed 6.257 |
| CP-67b | Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.1.1.file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch(System.Threading.CancellationToken).1.1.0 | Passed | 4.081 | 4.157 | No named spans; unattributed 4.157 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.annotated_reuse_resolves_only_an_earlier_filter_build.1.1.0 | Passed | 0.080 | 0.081 | No named spans; unattributed 0.081 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.C1098_card_1087_wrapper_census_row_is_split_under_the_same_cap.1.1.0 | Passed | 0.140 | 0.171 | No named spans; unattributed 0.171 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.unescapes_pipes_in_filters.1.1.0 | Passed | 0.079 | 0.117 | No named spans; unattributed 0.117 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.C1493_distinct_after_repeat_or_project_do_not_warn.1.1.0 | Passed | 0.078 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.conflicting_projects_cannot_share_an_output_identity.1.1.0 | Passed | 0.078 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.filter_shorthand_and_trailing_environment_prose_are_refused.1.1.0 | Passed | 0.074 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.invalid_serial_is_refused_before_execution.1.1.0 | Passed | 0.079 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.refuses_the_legacy_eight_column_table.1.1.0 | Passed | 0.057 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.maps_cp_reuse_to_the_same_build.1.1.0 | Passed | 0.089 | 0.120 | No named spans; unattributed 0.120 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.warns_when_estimate_exceeds_advisory_threshold.1.1.0 | Passed | 0.087 | 0.118 | No named spans; unattributed 0.118 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.derives_roster_tokens_from_the_filter.1.1.0 | Passed | 0.090 | 0.120 | No named spans; unattributed 0.120 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.C1493_DuplicateBuild_project_spellings_normalise.1.1.0 | Passed | 0.086 | 0.118 | No named spans; unattributed 0.118 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.C1493_DuplicateBuild_second_equivalent_build_warns_once.1.1.0 | Passed | 0.088 | 0.120 | No named spans; unattributed 0.120 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.C1493_DuplicateBuild_recommendation_imports_for_non_numeric_row_id.1.1.0 | Passed | 0.090 | 0.121 | No named spans; unattributed 0.121 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.cross_after_reuse_is_refused_without_a_relaxation_flag.1.1.0 | Passed | 0.076 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.unknown_duplicate_and_malformed_columns_are_refused.1.1.0 | Passed | 0.061 | 0.117 | No named spans; unattributed 0.117 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.command_rows_become_command_checkpoints.1.1.0 | Passed | 0.082 | 0.116 | No named spans; unattributed 0.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.imports_the_card_0688_table.1.1.0 | Passed | 0.013 | 0.013 | No named spans; unattributed 0.013 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.scheduler_passes_test_parallel_limit_only_to_serial_rows_process.1.1.0 | Passed | 0.230 | 0.274 | No named spans; unattributed 0.274 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.environment_reaches_only_its_row_and_its_reruns.1.1.0 | Passed | 0.406 | 0.440 | No named spans; unattributed 0.440 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.imports_the_card_0723_table.1.1.0 | Passed | 0.500 | 0.532 | No named spans; unattributed 0.532 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.C1493_warning_keeps_import_exit_and_output.1.1.0 | Passed | 0.503 | 0.535 | No named spans; unattributed 0.535 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.invalid_environment_names_and_duplicates_are_refused.1.1.0 | Passed | 0.505 | 0.538 | No named spans; unattributed 0.538 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.environment_map_round_trips_without_changing_serial.1.1.0 | Passed | 0.512 | 0.544 | No named spans; unattributed 0.544 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.serial_import_round_trips_and_excludes_other_rows.1.1.0 | Passed | 0.360 | 0.365 | No named spans; unattributed 0.365 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointImportTests.1.1.run_plan_and_import_resolve_the_committed_table_identically.1.1.0 | Passed | 2.300 | 2.369 | No named spans; unattributed 2.369 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.1.1.C1573_linux_only_cases_skip_when_stat_is_absent.1.1.0 | Passed | 8.739 | 8.784 | No named spans; unattributed 8.784 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests.1.1.note_returns_when_the_sink_resumes_on_the_caller_context(System.Threading.CancellationToken).1.1.0 | Passed | 0.030 | 0.036 | No named spans; unattributed 0.036 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests.1.1.fixture_child_whose_descendant_holds_a_pipe_fails_within_the_limit_and_is_killed(System.Threading.CancellationToken).1.1.0 | Passed | 2.210 | 2.213 | No named spans; unattributed 2.213 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests.1.1.fixture_git_blocked_opening_its_config_fails_within_the_limit(System.Threading.CancellationToken).1.1.0 | Passed | 2.113 | 2.116 | No named spans; unattributed 2.116 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.1.1.C1573_compiled_facade_seals_through_the_real_reader.1.1.0 | Passed | 4.697 | 4.704 | No named spans; unattributed 4.704 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests.1.1.repeat_dispose_completes_after_the_sink_dispose_faults.1.1.0 | Passed | 0.055 | 0.056 | No named spans; unattributed 0.056 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests.1.1.concurrent_callbacks_append_each_line_once_without_overlap(System.Threading.CancellationToken).1.1.0 | Passed | 0.007 | 0.007 | No named spans; unattributed 0.007 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests.1.1.terminal_report_waits_for_all_callbacks_and_log_flush.1.1.0 | Passed | 0.916 | 0.933 | No named spans; unattributed 0.933 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests.1.1.publication_failure_plus_log_close_failure_stays_invalid.1.1.0 | Passed | 0.361 | 0.364 | No named spans; unattributed 0.364 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests.1.1.log_io_failure_publishes_a_terminal_error_without_recursive_logging.1.1.0 | Passed | 0.566 | 0.582 | No named spans; unattributed 0.582 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointFreshOutputSealTests.1.1.C1573_embedded_inventory_seals_past_64_mib_and_refuses_inode_reuse.1.1.0 | Passed | 5.159 | 5.170 | No named spans; unattributed 5.170 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.1.1.C1532_script_stamp_matches_full_digest.1.1.0 | Passed | 13.844 | 13.863 | No named spans; unattributed 13.863 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.1.1.C1532_script_digest_limits_stalls_and_eof.1.1.0 | Passed | 22.775 | 22.776 | No named spans; unattributed 22.776 |
| CP-67c | Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests.1.1.C1532_scripts_are_ascii_and_parse_in_pwsh.1.1.0 | Passed | 10.951 | 10.953 | No named spans; unattributed 10.953 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLineTests.1.1.command_line_carries_exit.1.1.0 | Passed | 0.009 | 0.030 | No named spans; unattributed 0.030 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLineTests.1.1.tunit_line_matches_the_pinned_regex_with_slot_suffix.1.1.0 | Passed | 0.014 | 0.030 | No named spans; unattributed 0.030 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.after_selector_parses_ranges_and_all.1.1.0 | Passed | 0.019 | 0.047 | No named spans; unattributed 0.047 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLineTests.1.1.timeout_line_carries_timeout.1.1.0 | Passed | 0.009 | 0.030 | No named spans; unattributed 0.030 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.preparing_journal_precedes_all_run_io.1.1.0 | Passed | 0.197 | 0.221 | No named spans; unattributed 0.221 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.results_root_must_be_under_worktree.1.1.0 | Passed | 0.381 | 0.403 | No named spans; unattributed 0.403 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.rejects_reuse_across_different_after.1.1.0 | Passed | 0.384 | 0.405 | No named spans; unattributed 0.405 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.parses_yaml_and_defaults.1.1.0 | Passed | 0.382 | 0.403 | No named spans; unattributed 0.403 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.rejects_duplicate_or_unknown_build_ids.1.1.0 | Passed | 0.386 | 0.407 | No named spans; unattributed 0.407 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.rejects_backslash_or_trailing_space_output_path.1.1.0 | Passed | 0.396 | 0.422 | No named spans; unattributed 0.422 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointManifestTests.1.1.rejects_build_ids_that_are_not_isolated_output_directories.1.1.0 | Passed | 0.401 | 0.424 | No named spans; unattributed 0.424 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.partial_copy_failure_retains_exact_evidence_without_launch.1.1.0 | Passed | 1.490 | 1.563 | No named spans; unattributed 1.563 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.executor_ack_is_checked_before_work.1.1.0 | Passed | 1.565 | 1.597 | No named spans; unattributed 1.597 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.launch_intent_failure_prevents_native_launch.1.1.0 | Passed | 1.690 | 1.715 | No named spans; unattributed 1.715 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.definite_native_refusal_retains_evidence_with_known_outcome.1.1.0 | Passed | 1.732 | 1.795 | No named spans; unattributed 1.795 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.launch_does_not_write_a_shared_latest_pointer.1.1.0 | Passed | 1.699 | 1.915 | No named spans; unattributed 1.915 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.unknown_launch_outcome_is_never_rolled_back.1.1.0 | Passed | 1.699 | 1.960 | No named spans; unattributed 1.960 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.custody_write_failure_is_not_success.1.1.0 | Passed | 1.741 | 2.024 | No named spans; unattributed 2.024 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.starter_cannot_overwrite_child_progress.1.1.0 | Passed | 1.840 | 2.030 | No named spans; unattributed 2.030 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.late_executor_ack_resolves_launch_unknown.1.1.0 | Passed | 1.965 | 2.080 | No named spans; unattributed 2.080 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests.1.1.dead_executor_ack_recovers_without_progress.1.1.0 | Passed | 2.058 | 2.114 | No named spans; unattributed 2.114 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.1.1.C1532_content_and_receipt_limits_are_distinct.1.1.0 | Passed | 20.627 | 20.648 | No named spans; unattributed 20.648 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.1.1.C1532_regular_stamp_crosses_receipt_limit.1.1.0 | Passed | 3.866 | 3.875 | No named spans; unattributed 3.875 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.1.1.C1532_tail_edit_with_restored_mtime_is_tampered.1.1.0 | Passed | 6.764 | 6.796 | No named spans; unattributed 6.796 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.1.1.C1532_large_ignored_paths_remain_covered.1.1.0 | Passed | 5.992 | 6.030 | No named spans; unattributed 6.030 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.1.1.C1532_second_build_and_retry_admit_large_prior_output(System.Threading.CancellationToken).1.1.0 | Passed | 23.310 | 23.402 | No named spans; unattributed 23.402 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests.1.1.namespace_census_matches_compiled_checkpoint_cases.1.1.0 | Passed | 5.566 | 5.568 | No named spans; unattributed 5.568 |
| CP-67d | Antiphon.Tests.Checkpoints.CheckpointLargeStampTests.1.1.C1532_digest_preserves_verified_read_refusals.1.1.0 | Passed | 0.041 | 0.041 | No named spans; unattributed 0.041 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_accepted_filters_are_byte_preserved(System.String, System.String).1.1.0 | Passed | 0.359 | 0.459 | No named spans; unattributed 0.459 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_accepted_filters_are_byte_preserved(System.String, System.String).5.1.0 | Passed | 0.370 | 0.459 | No named spans; unattributed 0.459 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_accepted_filters_are_byte_preserved(System.String, System.String).2.1.0 | Passed | 0.355 | 0.459 | No named spans; unattributed 0.459 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_accepted_filters_are_byte_preserved(System.String, System.String).4.1.0 | Passed | 0.354 | 0.459 | No named spans; unattributed 0.459 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_method_or_without_each_suffix(System.String, System.Int32, System.Boolean).2.1.0 | Passed | 0.225 | 0.460 | No named spans; unattributed 0.460 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_method_or_without_each_suffix(System.String, System.Int32, System.Boolean).1.1.0 | Passed | 0.224 | 0.458 | No named spans; unattributed 0.458 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_manifest_validator_rejects_unsafe_or(System.String, System.String, System.Int32).1.1.0 | Passed | 0.361 | 0.460 | No named spans; unattributed 0.460 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_class_or_without_each_suffix(System.String, System.Int32).1.1.0 | Passed | 0.226 | 0.460 | No named spans; unattributed 0.460 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_manifest_validator_rejects_unsafe_or(System.String, System.String, System.Int32).2.1.0 | Passed | 0.365 | 0.459 | No named spans; unattributed 0.459 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_plan_entrypoints_refuse_before_work(System.String).3.1.0 | Passed | 0.626 | 0.673 | No named spans; unattributed 0.673 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_accepted_filters_are_byte_preserved(System.String, System.String).6.1.0 | Passed | 0.353 | 0.459 | No named spans; unattributed 0.459 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_method_or_without_each_suffix(System.String, System.Int32, System.Boolean).4.1.0 | Passed | 0.224 | 0.460 | No named spans; unattributed 0.460 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_accepted_filters_are_byte_preserved(System.String, System.String).3.1.0 | Passed | 0.370 | 0.476 | No named spans; unattributed 0.476 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_method_or_without_each_suffix(System.String, System.Int32, System.Boolean).3.1.0 | Passed | 0.000 | 0.001 | No named spans; unattributed 0.001 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_path_or_shape(System.String).1.1.0 | Passed | 0.000 | 0.001 | No named spans; unattributed 0.001 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_path_or_shape(System.String).2.1.0 | Passed | 0.000 | 0.001 | No named spans; unattributed 0.001 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_rejects_class_or_without_each_suffix(System.String, System.Int32).2.1.0 | Passed | 0.000 | 0.000 | No named spans; unattributed 0.000 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_plan_entrypoints_refuse_before_work(System.String).1.1.0 | Passed | 0.153 | 0.200 | No named spans; unattributed 0.200 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_plan_entrypoints_refuse_before_work(System.String).2.1.0 | Passed | 0.148 | 0.208 | No named spans; unattributed 0.208 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.exclusive_gate_returns_its_held_stream.1.1.0 | Passed | 0.487 | 0.492 | No named spans; unattributed 0.492 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.1.1.windows_confined_build_leaves_use_app_host_unset(System.Threading.CancellationToken).1.1.0 | NotExecuted | 0.178 | 0.192 | No named spans; unattributed 0.192 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nonselection_outcomes_keep_their_codes(System.String, System.Int32, System.String).4.1.0 | Passed | 1.669 | 1.726 | No named spans; unattributed 1.726 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nobuild_zero_execution_does_not_rebuild.1.1.0 | Passed | 1.672 | 1.727 | No named spans; unattributed 1.727 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_zero_execution_has_named_failure(System.Int32).2.1.0 | Passed | 1.680 | 1.735 | No named spans; unattributed 1.735 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_absent_inputs_preserve_existing_admission.1.1.0 | Passed | 1.676 | 1.735 | No named spans; unattributed 1.735 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_zero_execution_has_named_failure(System.Int32).3.1.0 | Passed | 1.684 | 1.737 | No named spans; unattributed 1.737 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_zero_execution_has_named_failure(System.Int32).1.1.0 | Passed | 1.683 | 1.738 | No named spans; unattributed 1.738 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nonselection_outcomes_keep_their_codes(System.String, System.Int32, System.String).1.1.0 | Passed | 1.692 | 1.738 | No named spans; unattributed 1.738 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nonselection_outcomes_keep_their_codes(System.String, System.Int32, System.String).2.1.0 | Passed | 1.681 | 1.739 | No named spans; unattributed 1.739 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nonselection_outcomes_keep_their_codes(System.String, System.Int32, System.String).5.1.0 | Passed | 1.683 | 1.740 | No named spans; unattributed 1.740 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nonselection_outcomes_keep_their_codes(System.String, System.Int32, System.String).6.1.0 | Passed | 1.685 | 1.741 | No named spans; unattributed 1.741 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_nonselection_outcomes_keep_their_codes(System.String, System.Int32, System.String).3.1.0 | Passed | 1.679 | 1.743 | No named spans; unattributed 1.743 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.foreign_pid_namespace_is_not_local_dead.1.1.0 | Passed | 1.028 | 1.035 | No named spans; unattributed 1.035 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.foreign_host_is_not_local_dead.1.1.0 | Passed | 1.039 | 1.046 | No named spans; unattributed 1.046 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.foreign_boot_is_not_local_dead.1.1.0 | Passed | 1.039 | 1.047 | No named spans; unattributed 1.047 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.unknown_probe_is_a_veto.1.1.0 | Passed | 1.011 | 1.058 | No named spans; unattributed 1.058 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.same_live_generation_is_a_veto.1.1.0 | Passed | 1.011 | 1.063 | No named spans; unattributed 1.063 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.reused_pid_is_retained_and_never_signaled.1.1.0 | Passed | 1.011 | 1.062 | No named spans; unattributed 1.062 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.1.1.confirmed_dead_local_identity_permits_cleanup.1.1.0 | Passed | 1.092 | 1.115 | No named spans; unattributed 1.115 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOrFilterTests.1.1.C0931_shared_build_report_preserves_zero_failure.1.1.0 | Passed | 2.026 | 2.081 | No named spans; unattributed 2.081 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.1.1.unevaluable_import_is_refused_before_the_build(System.Threading.CancellationToken).1.1.0 | Passed | 13.698 | 13.706 | No named spans; unattributed 13.706 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.1.1.transitive_import_build_preserves_the_tracked_dependency(System.Threading.CancellationToken).1.1.0 | Passed | 39.748 | 39.782 | No named spans; unattributed 39.782 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.1.1.conditional_import_build_preserves_the_tracked_dependency(System.Threading.CancellationToken).1.1.0 | Passed | 29.703 | 29.715 | No named spans; unattributed 29.715 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.1.1.imported_props_build_preserves_the_tracked_dependency(System.Threading.CancellationToken).1.1.0 | Passed | 27.864 | 27.879 | No named spans; unattributed 27.879 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.disk_ledger_cannot_authorize_user_deletion(System.String).1.1.0 | Passed | 0.094 | 0.097 | No named spans; unattributed 0.097 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.1.1.directory_build_props_build_preserves_the_tracked_dependency(System.Threading.CancellationToken).1.1.0 | Passed | 26.416 | 26.428 | No named spans; unattributed 26.428 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.changed_inventory_is_refused(System.String).4.1.0 | Passed | 0.059 | 0.061 | No named spans; unattributed 0.061 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.changed_inventory_is_refused(System.String).3.1.0 | Passed | 0.072 | 0.076 | No named spans; unattributed 0.076 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.changed_inventory_is_refused(System.String).2.1.0 | Passed | 0.099 | 0.102 | No named spans; unattributed 0.102 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.changed_inventory_is_refused(System.String).1.1.0 | Passed | 0.102 | 0.106 | No named spans; unattributed 0.106 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.disk_ledger_cannot_authorize_user_deletion(System.String).4.1.0 | Passed | 0.097 | 0.101 | No named spans; unattributed 0.101 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.disk_ledger_cannot_authorize_user_deletion(System.String).2.1.0 | Passed | 0.089 | 0.092 | No named spans; unattributed 0.092 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.unchanged_owned_inventory_is_removed.1.1.0 | Passed | 0.188 | 0.191 | No named spans; unattributed 0.191 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.1.1.disk_ledger_cannot_authorize_user_deletion(System.String).3.1.0 | Passed | 0.090 | 0.094 | No named spans; unattributed 0.094 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.linux_fifo_receipts_refuse_within_four_seconds.1.1.0 | Passed | 6.073 | 6.144 | No named spans; unattributed 6.144 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.symlink_and_directory_receipts_refuse_within_four_seconds.1.1.0 | Passed | 7.904 | 7.977 | No named spans; unattributed 7.977 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.malformed_collection_and_string_shapes_return_named_refusals.1.1.0 | Passed | 15.874 | 18.878 | No named spans; unattributed 18.878 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.receipt_row_and_build_identities_refuse_blank_without_writing(System.String).1.1.0 | Passed | 17.066 | 17.261 | No named spans; unattributed 17.261 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.receipt_row_and_build_identities_refuse_blank_without_writing(System.String).2.1.0 | Passed | 22.130 | 22.325 | No named spans; unattributed 22.325 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.receipt_row_and_build_identities_refuse_blank_without_writing(System.String).3.1.0 | Passed | 19.313 | 19.496 | No named spans; unattributed 19.496 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.receipt_row_and_build_identities_refuse_blank_without_writing(System.String).4.1.0 | Passed | 25.285 | 25.444 | No named spans; unattributed 25.444 |
| CP-67e | Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests.1.1.malformed_state_shape_by_verb_table_matches_documented_exits.1.1.0 | Passed | 50.539 | 51.027 | No named spans; unattributed 51.027 |


### Published slices and local closure

- `b4cea3d0e1d3f9e47af86095696a72a25bcd530a`: plan/skip classification and CP-67 pre-run diagnosis; pushed before the long product run.
- `6fb9409ebf0650f15a5c4d111cd8664fd29a555f`: measured per-row investigation and corrected plan; pushed. This report is a later documentation-only commit on the same assigned branch; its final 40-hex publication receipt is supplied in the caller-facing message.

Before the report-only commit, a fresh fetch of origin/master at `31f990317038635c2f1e652b6a950905fcf07a31` and `git merge-tree --write-tree origin/master HEAD` passed without conflicts (tree `08ea7180360def1e59204fcc9acb92b88af1964e`). Nothing was merged or rebased. The evidence-history guard over base 0219ccebf3982b191116dd7d731c8d5b0aaa97c1 through `6fb9409ebf0650f15a5c4d111cd8664fd29a555f` passed: 2 commits / 0 evidence entries / 0 violations. The final report commit receives its own whole-history guard and remote-tip check before settlement.

Final plan table SHA256 `292b8f4206db688581964686897dd2ff5ad5f5c27fed125063b680f1f8dc838f`; plan bytes SHA256 `e428debcaf30c537fb498ffb48422bdfb8f5d6c998a6a57c28fc7cbfaaffad6a`. The table is byte-identical to the 97-row imported candidate; prose spacing alone was corrected afterward. Generated YAML/JSON/TRX/logs/journals/archives remain ignored in their original evidence roots. Only this individual Markdown task report is force-added under the existing permitted Markdown exception.

After every run, observer, audit and report validator had finished, explicit manual inspection found no owned test/checkpoint driver. Exact producer-owned output roots were canonical, link-free and contained no tracked paths. Each regular file was inventoried and its identity/size/content rechecked immediately before unlinking; only empty, same-identity directories were removed. This was manual task cleanup with explicit ownership, not a fabricated in-memory cleanup context. All three outputs are absent:

| Literal owned output root | Files removed | Directories removed | Bytes removed |
|---|---:|---:|---:|
| /work/worktrees/task-698bf6b9/src/Antiphon.HostCleanup/bin-c1610-p2-tool | 3 | 1 | 495248 |
| /work/worktrees/task-698bf6b9/tests/Antiphon.Tests/bin-c1610-p2 | 2727 | 465 | 952722703 |
| /work/worktrees/task-698bf6b9/tools/Antiphon.Checkpoints/bin-c1610-p2-tool | 40 | 18 | 20784868 |

All evidence was retained. No desktop files, live qualification d47848cc, C:\Antiphon content, definitions, production databases, tags, deployment or activation were touched. The bounded Debug stops with the completed rows published and the remaining gates explicit, within its 150-minute task budget.

--- next stage ---
next: code
handoff: CP-79 (499 identities) stopped and CP-80 (168 identities) timed out; address repeated C1008 fixture/model setup or plan exact method partitions before repeating, preserving assertions and all coverage. Resolve the planned-Windows-skip report-validator contract and arrange separately authorized native witnesses. Joint Opus Review of S2+S3 only once every required gate is green; no S2-only land.
artifact: /work/worktrees/task-698bf6b9/.antiphon/task-698bf6b9.md

[antiphon-report:698bf6b9 done]
