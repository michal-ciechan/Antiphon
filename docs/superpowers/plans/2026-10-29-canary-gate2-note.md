# Gate-2 canary: CARD-1589
CARD-1589 concerns cache Fixture preflight rejecting a supported absent, offline temp deployment hold.
The card records an older failure; current `scripts/c590-remote.sh` accepts the non-retiring hold only with zero work and complete host-container absence.
Next concrete step: review the existing CARD-1589 fix and its Fixture/Inventory receipts at the reviewed source SHA, preserving refusal of live, busy, or unknown temp state.
This canary changes documentation only; it does not execute rollout gates or establish cache-fixture acceptance.
