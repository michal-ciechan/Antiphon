# Gate 9 · Retirement audit and CRLF checkouts

This note belongs to the Windows Full run `79d9e6f9` failure set ([run note](2026-11-22-rc-run-79d9e6f9-failures.md)). It is kept in its own file so the CARD-2057 retirement audit chain does not edit the shared run note.

The audit fixtures seeded plain LF content. Their linked-checkout cases exercised Git directory layout with the same bytes in the worktree and index.
The Windows launcher added an indexed LF blob with `text eol=crlf`, so a real Git checkout on Linux first exposed the false dirty result on the rollout volume.
For future checkout conversions, commit the attribute and let Git create the linked checkout. Check the byte difference and require Ready for the clean checkout and refusal for both a content edit and the same conversion on an undeclared path.
