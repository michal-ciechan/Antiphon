import { Alert, Button, Select, Stack, Text, Textarea } from '@mantine/core'
import { useState } from 'react'
import { usePrivateNotes, type CardFileStatus, type CardFileVisibility } from '../../api/cardFiles'

export const PRIVATE_NOTES_LABEL = 'Private notes (kept in Antiphon; excluded from card files)'
export function CardFileFields({ visibility, onVisibility, notes, onNotes, error, visibilityError }: {
  visibility: CardFileVisibility; onVisibility: (value: CardFileVisibility) => void
  notes: string; onNotes: (value: string) => void; error?: string; visibilityError?: string
}) {
  return <Stack gap="xs">
    <Text size="xs">Title, description, outcome and archive reasons are public fields on eligible cards. Public override still requires board opt-in and configured repository visibility.</Text>
    <Select label="Card-file visibility" data={['Inherit', 'Private', 'Public']} value={visibility} error={visibilityError} onChange={(v) => onVisibility((v as CardFileVisibility) ?? 'Inherit')} />
    <Textarea label={PRIVATE_NOTES_LABEL} value={notes} onChange={(e) => onNotes(e.currentTarget.value)} autosize minRows={3}
      error={error ?? (notes.length > 20000 ? 'Private notes must be at most 20000 characters.' : undefined)} />
  </Stack>
}
export function CardFilePolicyText({ status }: { status?: CardFileStatus | null }) {
  if (!status) return <Text size="xs">status unavailable; export safety not confirmed</Text>
  const target = status.repositoryPath && status.directory ? `${status.repositoryPath.replace(/[\\/]$/, '')}\\${(status.relativeFile ?? status.directory).replaceAll('/', '\\')}` : 'unavailable (no safe resolved card-file target)'
  return <Stack gap={3}>
    <Text size="sm">{status.eligible ? `Eligible: ${status.intervalSeconds === 0 ? 'manual sync' : `next sync (${status.intervalSeconds}s)`}` : `Not written: ${status.reason}`}</Text>
    <Text size="xs">Target: {target}</Text>
    <Text size="xs">{status.repositoryVisibility === 'Unknown' ? 'Unknown repository visibility; not checked. Sync blocked.' : `Configured ${status.repositoryVisibility} repository`}</Text>
    {status.repositoryVisibility === 'Public' && <Alert color="orange">PUBLIC REPOSITORY: public card fields will be written on sync</Alert>}
    {status.removalPending && <Alert color="orange">{status.workingTreeRemovalPending === null || status.gitRemovalPending === null
      ? 'Cleanup state unavailable; erasure not confirmed'
      : status.warnings.includes('card_file_staged_private_residue') ? 'Staged private export: reconcile to unstage'
        : status.workingTreeRemovalPending ? 'Pending: reconcile previously exported working files' : 'Working files removed; Git index/HEAD cleanup required'}. Git history is retained.</Alert>}
    {!status.enabled && <Text size="sm">Card-file sync disabled; existing exports are not erased.</Text>}
    {status.warnings.includes('card_files_ignore_missing') && <Text size="sm">Protect docs/cards with the documented default ignore rule before opting in.</Text>}
    {status.ignored && <Text size="sm">Target is ignored. Review and add an exact board exception in .gitignore to allow publication.</Text>}
  </Stack>
}
export function PrivateNotesPanel({ cardId, revisionNumber }: { cardId: string; revisionNumber?: number }) {
  const [opened, setOpened] = useState(false)
  const notes = usePrivateNotes(cardId, opened, revisionNumber)
  return <Stack gap="xs">
    <Button variant="subtle" onClick={() => setOpened(!opened)}>{opened ? 'Hide private notes' : revisionNumber === undefined ? 'Open private notes' : 'Inspect private-note snapshot'}</Button>
    {opened && <><Text size="sm">{PRIVATE_NOTES_LABEL}</Text>
      {notes.isError ? <Alert color="red">Private notes could not be read.</Alert> : notes.isPending ? <Text>Loading private notes...</Text>
        : <Text component="pre" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{notes.data.privateNotes === null ? 'Private-note history unknown' : notes.data.privateNotes === '' ? 'No private notes' : notes.data.privateNotes}</Text>}
    </>}
  </Stack>
}
