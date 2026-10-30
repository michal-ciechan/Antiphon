# CARD-1514 CP-8 timing investigation

Assigned base: eaea94cffa8363f6074d51e146916992545a32da. Linux/server2.
Verification is incomplete while this investigation runs. Historical CP-8 failures
remain failures; no production deadline or verdict assertion is authorized to change
without evidence. Measurements and raw receipts are retained in
`.antiphon/79c0c349-evidence/`.

The FIFO predicate times `Read-TestPhaseJsonlBounded` followed by
`Get-QualificationPhaseSums` together. Its 0.2-second outer assertion is test-only:
the first reader receives 200 ms, while the second has a separate default 500 ms
watch. The launch witness combines child exit, exit code, launch marker and a
12-second body ceiling inside a 12-second parent process ceiling.

Measurements, structural controls and final checkpoint outcomes will be appended
after execution. No clean result is claimed by this initial record.
