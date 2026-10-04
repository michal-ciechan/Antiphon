// Ordinary owned-Docker lane only. Reuse the production-shaped C1008 materializer
// and lifecycle driver; C994 selects its own exact ten-outcome roster and ledger.
process.env.C994_REAL='1';
await import('./c1008-recycle-real-cases.mjs');
