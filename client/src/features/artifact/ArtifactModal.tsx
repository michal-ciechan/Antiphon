import {
	Modal,
	Group,
	Text,
	Badge,
	Box,
	ActionIcon,
	Tooltip,
	Loader,
	Stack,
	Tabs,
	ScrollArea,
} from '@mantine/core'
import { VscFile, VscEdit, VscEye } from 'react-icons/vsc'
import { useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { TipTapEditor } from './TipTapEditor'
import { ArtifactDiffViewer } from './ArtifactDiffViewer'
import { MarkdownSectionTree } from './MarkdownSectionTree'
import { useStageArtifact } from '../../api/artifacts'
import type { ArtifactDto } from '../workflow/types'

interface ArtifactModalProps {
	artifact: ArtifactDto | null
	workflowId: string | undefined
	onClose: () => void
}

type TabValue = 'review' | 'rendered' | 'source' | 'diff'

export function ArtifactModal({ artifact, workflowId, onClose }: ArtifactModalProps) {
	const [editable, setEditable] = useState(false)
	const [tab, setTab] = useState<TabValue>('review')

	const { data: fullArtifact, isLoading } = useStageArtifact(
		workflowId,
		artifact?.stageId,
		artifact?.version,
	)

	// Load previous version for diff (only if version > 1)
	const prevVersion = artifact && artifact.version > 1 ? artifact.version - 1 : undefined
	const { data: prevArtifact } = useStageArtifact(
		workflowId,
		artifact?.stageId,
		prevVersion,
	)

	const content = fullArtifact?.content ?? ''
	const prevContent = prevArtifact?.content ?? ''

	function handleClose() {
		setEditable(false)
		onClose()
	}

	return (
		<Modal
			opened={!!artifact}
			onClose={handleClose}
			size="90vw"
			styles={{
				body: { padding: 0, display: 'flex', flexDirection: 'column', height: '85vh' },
				content: { display: 'flex', flexDirection: 'column', height: '85vh' },
			}}
			title={
				artifact && (
					<Group gap="sm" wrap="nowrap">
						<VscFile size={16} />
						<Text fw={600} size="sm" style={{ fontFamily: 'monospace' }}>
							{artifact.fileName}
						</Text>
						{artifact.isPrimary && (
							<Badge size="xs" color="active" variant="light">Primary</Badge>
						)}
						<Badge size="xs" color="gray" variant="outline">
							{artifact.stageName} · v{artifact.version}
						</Badge>
					</Group>
				)
			}
		>
			{artifact && (
				<Box style={{ display: 'flex', flexDirection: 'column', flex: 1, overflow: 'hidden' }}>
					{isLoading ? (
						<Stack align="center" justify="center" style={{ flex: 1 }}>
							<Loader size="sm" />
							<Text c="dimmed" size="sm">Loading artifact…</Text>
						</Stack>
					) : (
						<Tabs
							value={tab}
							onChange={v => setTab(v as TabValue)}
							style={{ display: 'flex', flexDirection: 'column', flex: 1, overflow: 'hidden' }}
							styles={{ panel: { flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column' } }}
						>
							<Tabs.List px="sm" style={{ borderBottom: '1px solid var(--mantine-color-dark-4)', flexShrink: 0 }}>
								<Tabs.Tab value="review">Review</Tabs.Tab>
								<Tabs.Tab value="rendered">Rendered</Tabs.Tab>
								<Tabs.Tab value="source">Source</Tabs.Tab>
								{artifact.version > 1 && (
									<Tabs.Tab value="diff">
										Diff vs v{artifact.version - 1}
									</Tabs.Tab>
								)}
								<Box style={{ flex: 1 }} />
								<Tooltip label={editable ? 'Switch to view mode' : 'Switch to edit mode'}>
									<ActionIcon
										variant={editable ? 'filled' : 'subtle'}
										color={editable ? 'active' : 'gray'}
										size="sm"
										my="auto"
										onClick={() => { setEditable(e => !e); setTab('rendered') }}
									>
										{editable ? <VscEye size={14} /> : <VscEdit size={14} />}
									</ActionIcon>
								</Tooltip>
							</Tabs.List>

							{/* Review tab — section tree with hash-based review state */}
							<Tabs.Panel value="review">
								<ScrollArea style={{ flex: 1, height: '100%' }}>
									{workflowId && (
										<MarkdownSectionTree
											content={content}
											workflowId={workflowId}
											stageExecutionId={artifact.id}
										/>
									)}
								</ScrollArea>
							</Tabs.Panel>

							{/* Rendered tab — react-markdown or TipTap in edit mode */}
							<Tabs.Panel value="rendered">
								{editable ? (
									<TipTapEditor
										key={`${artifact.id}-${artifact.version}`}
										content={content}
										editable={true}
									/>
								) : (
									<ScrollArea style={{ flex: 1, height: '100%' }} p="md">
										<Box
											style={{
												maxWidth: 760,
												margin: '0 auto',
												padding: '16px 24px',
												fontSize: 14,
												lineHeight: 1.7,
											}}
										>
											<ReactMarkdown remarkPlugins={[remarkGfm]}>
												{content}
											</ReactMarkdown>
										</Box>
									</ScrollArea>
								)}
							</Tabs.Panel>

							{/* Source tab — raw markdown text */}
							<Tabs.Panel value="source">
								<ScrollArea style={{ flex: 1, height: '100%' }}>
									<Box
										component="pre"
										style={{
											margin: 0,
											padding: '16px',
											fontFamily: 'var(--mantine-font-family-monospace)',
											fontSize: '0.8rem',
											lineHeight: 1.6,
											whiteSpace: 'pre-wrap',
											wordBreak: 'break-all',
											color: 'var(--mantine-color-gray-4)',
										}}
									>
										{content}
									</Box>
								</ScrollArea>
							</Tabs.Panel>

							{/* Diff tab — compare with previous version */}
							{artifact.version > 1 && (
								<Tabs.Panel value="diff">
									<ArtifactDiffViewer
										oldContent={prevContent}
										newContent={content}
										oldLabel={`v${artifact.version - 1}`}
										newLabel={`v${artifact.version}`}
									/>
								</Tabs.Panel>
							)}
						</Tabs>
					)}
				</Box>
			)}
		</Modal>
	)
}
