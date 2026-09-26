import { Table, Text } from '@mantine/core'
import { HOST_WINDOWS, type HostWindowRollups } from '../../api/hosts'

const bytes = (value: number) => `${(value / 1_000_000_000).toFixed(1)} GB`
const percent = (value: number) => `${value.toFixed(0)} %`
const load = (value: number) => value.toFixed(2)

export function RollupTable({ rollups }: { rollups: Record<string, HostWindowRollups> | null }) {
  if (!rollups) return <Text size="sm" c="dimmed">No rollups</Text>
  return (
    <Table striped highlightOnHover withTableBorder fz="xs" aria-label="Host rollups">
      <Table.Thead><Table.Tr><Table.Th>Window</Table.Th><Table.Th>CPU avg / max</Table.Th><Table.Th>Load avg / max</Table.Th><Table.Th>Memory avg / max</Table.Th></Table.Tr></Table.Thead>
      <Table.Tbody>
        {HOST_WINDOWS.map(window => {
          const values = rollups[window]
          const cells = [
            values?.cpuPercent ? `${percent(values.cpuPercent.avg)} / ${percent(values.cpuPercent.max)}` : '—',
            values?.load1 ? `${load(values.load1.avg)} / ${load(values.load1.max)}` : '—',
            values?.memoryUsedBytes ? `${bytes(values.memoryUsedBytes.avg)} / ${bytes(values.memoryUsedBytes.max)}` : '—',
          ]
          return <Table.Tr key={window} className={window === '5m' || window === '15m' ? 'hosts-rollup-wide' : undefined}>
            <Table.Th scope="row">{window}</Table.Th>{cells.map((cell, i) => <Table.Td key={i}>{cell}</Table.Td>)}
          </Table.Tr>
        })}
      </Table.Tbody>
    </Table>
  )
}
