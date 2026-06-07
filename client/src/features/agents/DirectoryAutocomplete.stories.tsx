import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState } from 'react'
import type { DirectoryBrowseResponse } from '../../api/filesystem'
import { filesystemKeys } from '../../api/filesystem'
import { DirectoryAutocomplete } from './DirectoryAutocomplete'

/**
 * The preview only supplies a MantineProvider, so each story is wrapped in a
 * QueryClientProvider whose cache is pre-seeded for the relevant paths. The hook
 * (`useDirectoryBrowse`) then reads cached data and never hits the network — there is
 * no MSW in Storybook. The query key factory is `['filesystem','browse', path]`.
 */
function withSeededQueryClient(seed: Array<[string, DirectoryBrowseResponse]>) {
  return function Decorator(Story: () => React.ReactElement) {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity, gcTime: Infinity } },
    })
    for (const [path, data] of seed) {
      client.setQueryData(filesystemKeys.browse(path), data)
    }
    return (
      <QueryClientProvider client={client}>
        <Story />
      </QueryClientProvider>
    )
  }
}

/** Interactive container so value/createIfMissing update on type and toggle. */
function Demo({ initialValue }: { initialValue: string }) {
  const [value, setValue] = useState(initialValue)
  const [createIfMissing, setCreateIfMissing] = useState(false)
  return (
    <div style={{ maxWidth: 480 }}>
      <DirectoryAutocomplete
        value={value}
        onChange={setValue}
        createIfMissing={createIfMissing}
        onCreateIfMissingChange={setCreateIfMissing}
      />
    </div>
  )
}

const meta: Meta<typeof DirectoryAutocomplete> = {
  title: 'Agents/DirectoryAutocomplete',
  component: DirectoryAutocomplete,
}
export default meta

type Story = StoryObj<typeof DirectoryAutocomplete>

/**
 * Empty value: the hook is enabled here because a real input would be focused. The
 * seeded drives listing populates the suggestion dropdown (C:/, D:/). Click the input
 * to expand it.
 */
export const EmptyShowsDrives: Story = {
  decorators: [
    withSeededQueryClient([
      [
        '',
        { normalizedPath: '', exists: false, isDrivesListing: true, suggestions: ['C:/', 'D:/'] },
      ],
    ]),
  ],
  render: () => <Demo initialValue="" />,
}

/** Typing a prefix surfaces matching child directories. */
export const PrefixShowsChildren: Story = {
  decorators: [
    withSeededQueryClient([
      [
        'C:/sr',
        {
          normalizedPath: 'C:/sr',
          exists: false,
          isDrivesListing: false,
          suggestions: ['C:/src', 'C:/srv'],
        },
      ],
    ]),
  ],
  render: () => <Demo initialValue="C:/sr" />,
}

/** A non-existent path shows the orange warning and the create toggle. */
export const MissingPathShowsWarningAndToggle: Story = {
  decorators: [
    withSeededQueryClient([
      [
        'C:/nope',
        { normalizedPath: 'C:/nope', exists: false, isDrivesListing: false, suggestions: [] },
      ],
    ]),
  ],
  render: () => <Demo initialValue="C:/nope" />,
}

/** An existing path renders no warning. */
export const ExistingPathNoWarning: Story = {
  decorators: [
    withSeededQueryClient([
      [
        'C:/src',
        {
          normalizedPath: 'C:/src',
          exists: true,
          isDrivesListing: false,
          suggestions: ['C:/src'],
        },
      ],
    ]),
  ],
  render: () => <Demo initialValue="C:/src" />,
}
