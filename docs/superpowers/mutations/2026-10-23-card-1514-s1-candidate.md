# CARD-1514 S1 unbound Mutation candidate

Code/landing owner: f35fcce8. S1 mechanically extracts sixteen existing scenarios
from one method into four methods in RemoteScriptContractTests, preserving their
assertions, actions, strict limiter and native-Linux guards. No new optimization
or helper is introduced, and the landed plan defines zero S1 PC rows. SourceLanding
Mutation owns missing-control discovery even for this zero-PC slice. Do not count
ordinary green as mutation evidence. S3 PC-3a/b/c/d remain independently pending.

Verification: [S1 manifest](../plans/2026-10-23-card-1514-s1-code-verification.md).
