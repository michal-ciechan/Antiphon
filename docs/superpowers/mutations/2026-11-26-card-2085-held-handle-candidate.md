# CARD-2085 unbound Mutation candidate

Original Code and landing owner `943b3cf8-7104-4378-b38b-fb1225d9029e`.
Bind the candidate to the published source after ordinary Review and landing.
All four controls are pending. The red-first witnesses are ordinary Code
evidence. SourceLanding Mutation runs each edit, restores it, and reruns the
same exact method green.

| PC | Source variant | Exact filter | Expected red |
|---|---|---|---|
| PC-1 | After `Io.Open` in `SafeWrite.cs`, restore the path identity probe for the gate mode alone. | `/*/Antiphon.Tests.Checkpoints/CheckpointHeldHandleTests/gate_and_append_record_identity_without_reopening` | The gate argument observes one exclusive path probe, where zero is required. |
| PC-2 | After `Io.Open`, restore the path identity probe for the append mode alone. | `/*/Antiphon.Tests.Checkpoints/CheckpointHeldHandleTests/gate_and_append_record_identity_without_reopening` | The append argument observes one exclusive path probe, where zero is required. |
| PC-3 (M2) | Remove creation registration from the successful `OpenAndRemember` handoff. | `/*/Antiphon.Tests.Checkpoints/CheckpointWarmBuildTests/cache_file_creation_does_not_reopen_its_exclusive_handle` | `write.WasCreated(path)` is false for the file created by the retained handle. |
| PC-4 | Remove disposal in the `OpenAndRemember` exception path. | `/*/Antiphon.Tests.Checkpoints/CheckpointHeldHandleTests/open_create_disposes_the_stream_when_recording_identity_throws` | The captured native handle is still open after the injected exception. |

For PC-1 and PC-2 independently record which argument failed. The sibling
argument in the same method is the unaffected control. Keep the checkpoint
output cleanup changes owned by CARD-2062 and CARD-2052 outside this pack.
