Design verification for the landed plan. Read it first; append the structure below; do not rewrite the fix design.
Every guard that protects a safety-critical assertion gets a PC-n positive control.
Read every touched test body and relevant fixture/helper before naming cases. For new files read the nearest fixture or record its absence and setup. Account for boundary combinations or justify exclusion.

## Verification design
### Inspection
- <test/fixture bodies read> | <boundaries -> V/R IDs or exclusion>
### Delivery inventory
For every new or changed asynchronous outcome-delivery path, enumerate producer, destination,
persistence boundary, recovery, and observable receipt. Name the durable identity that connects
them. Include a producer-to-recipient test through the real queue/delivery path, covering a busy
recipient and one already eligible to receive, plus crash/enqueue-failure recovery at each
handoff. An accepted request, queue insert, terminal business event, Sent flag or transport
acknowledgement alone must not satisfy delivery acceptance. For session input require the
matching complete UserPrompt transcript evidence. Declare substitutes and what each cannot
prove. Test-design review must reject a design that stops before recipient evidence. Every
safety-critical delivery/recovery guard needs a named positive control.
### Proves it works now
- V-1: <behaviour> | <layer> | <test/command> | <expected>
### Guards the regression
- R-1: <regression> | <test and decisive assertion>
### Guard inventory
- G-1: <plan reference + safety-critical guard/invariant> | PC-1
Inventory every safety-critical guard, including untested ones; split independently bypassable guards. Map each 1:1 to a distinct defined PC-n. Justify none.
### Positive controls
- PC-1: break <G-1> by <compiling defect>; expect <exact method> red at <assertion>.
  Build reports break, red, restore, green for each.
### Out of scope
- <exclusion and reason>
### Cost
- suites/filters; numeric total minutes = setup/build + V/R + every PC red/restore/green; label estimated/measured. Quantify savings; justify zero.

Before handoff record: bodies read, boundaries covered; guards=N, mapped=N, missing=0, duplicate PC mappings=0; all PCs defined/executable; numeric Cost breakdown. Finish omissions; no placeholders/TBD.
next: code only when complete; plan for an unverifiable seam; decide for a human choice. Commit and push the plan doc.
