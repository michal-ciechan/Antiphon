# Debug b9ee02c4: C849 red-m6

Start/source: 13565c5a480d8f30a52aa4679a5f1681ef2a8c70, Linux runner mirror.
Assigned scope: reproduce C849 alone 20 times, compare pre-repair fixture, repair the wrong side without weakening custody assertions; run C84x, roster/census selections and land graph Check.

Initial inspection: m6 replaces the INT handler with an early return. red-m6 is 1 only if the group witness is installed and its full release contract fails. A failure before arming cannot count as a detected mutation. No production defect is attributed yet.

Evidence is kept under .antiphon/evidence/b9ee02c4/ (ignored). Native tests and live qualification are outside this task.
