import { Alert, Button, Group, Paper, Stack, Switch, Text } from '@mantine/core'
import { useState } from 'react'
import { useCardFileSettings, useCardFileStatus, useSyncCardFiles } from '../../api/cardFiles'
import { getApiErrorMessage, getApiFieldErrors } from '../../api/client'
import { CardFilePolicyText } from './CardFilePrivacy'

export function BoardCardFileSettings({ boardId }: { boardId: string }) {
  const status = useCardFileStatus(boardId)
  const save = useCardFileSettings(boardId)
  const sync = useSyncCardFiles(boardId)
  const [choice, setChoice] = useState<boolean | null>(null)
  return <Paper withBorder p="sm" mb="sm"><Stack gap="xs">
    <Switch label="Publish card files" checked={choice ?? status.data?.syncCardFiles ?? false} onChange={(e) => setChoice(e.currentTarget.checked)} disabled={!status.data} />
    <Text size="xs">Applies to current and future Inherit cards. Private cards and private notes are excluded. Turning off requires reconciliation of existing exports.</Text>
    <CardFilePolicyText status={status.data} />
    {(status.error || save.error || sync.error) && <Alert color="red">{getApiErrorMessage(status.error ?? save.error ?? sync.error, 'Card-file status unavailable')}{Object.values(getApiFieldErrors(save.error)).join('; ')}</Alert>}
    <Group><Button disabled={!status.data || choice === null} loading={save.isPending} onClick={() => save.mutate({ syncCardFiles: choice!, expectedSyncCardFiles: status.data!.syncCardFiles }, { onSuccess: () => setChoice(null) })}>Save publishing policy</Button>
      <Button variant="light" loading={sync.isPending} onClick={() => sync.mutate(true)}>Preview sync</Button>
      <Button variant="light" loading={sync.isPending} onClick={() => sync.mutate(false)}>Sync card files</Button></Group>
    {sync.data && <Text size="sm">{sync.data.dryRun ? 'Preview' : 'Sync'}: {sync.data.writeSkipReason ? `Not written: ${sync.data.writeSkipReason}` : sync.data.dryRun ? 'Planned changes' : 'Reconciled'}; eligible {sync.data.eligibleCards}; {sync.data.dryRun ? 'would write' : 'written'} {sync.data.written}; {sync.data.dryRun ? 'would delete' : 'deleted'} {sync.data.deleted}. {sync.data.error}</Text>}
  </Stack></Paper>
}
