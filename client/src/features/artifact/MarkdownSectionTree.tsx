import { useEffect, useState, useCallback } from 'react'
import {
	Box,
	Text,
	Group,
	Badge,
	ActionIcon,
	Collapse,
	Loader,
	Tooltip,
	Stack,
} from '@mantine/core'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { VscCheck, VscRefresh, VscChevronRight, VscChevronDown } from 'react-icons/vsc'
import { parseSections, attachHashes, type Section } from './parseSections'
import {
	useSectionReviews,
	useMarkSectionReviewed,
	useUnmarkSectionReviewed,
	type SectionReviewDto,
} from '../../api/artifacts'
import classes from './MarkdownSectionTree.module.css'

interface Props {
	content: string
	workflowId: string
	stageExecutionId: string
}

export function MarkdownSectionTree({ content, workflowId, stageExecutionId }: Props) {
	const [sections, setSections] = useState<Section[]>([])
	const [hashing, setHashing] = useState(true)

	const { data: reviews = [] } = useSectionReviews(workflowId, stageExecutionId)

	useEffect(() => {
		setHashing(true)
		const tree = parseSections(content)
		attachHashes(tree).then(() => {
			setSections(tree)
			setHashing(false)
		})
	}, [content])

	const reviewMap = new Map<string, SectionReviewDto>(reviews.map(r => [r.sectionPath, r]))

	if (hashing) {
		return (
			<Group p="md">
				<Loader size="xs" />
				<Text size="sm" c="dimmed">Parsing sections…</Text>
			</Group>
		)
	}

	if (sections.length === 0) {
		return (
			<Box p="md">
				<Text size="sm" c="dimmed">No headings found — document has no sections.</Text>
			</Box>
		)
	}

	return (
		<Stack gap={0}>
			{sections.map(s => (
				<SectionNode
					key={s.path}
					section={s}
					reviewMap={reviewMap}
					workflowId={workflowId}
					stageExecutionId={stageExecutionId}
					depth={0}
				/>
			))}
		</Stack>
	)
}

interface NodeProps {
	section: Section
	reviewMap: Map<string, SectionReviewDto>
	workflowId: string
	stageExecutionId: string
	depth: number
}

function SectionNode({ section, reviewMap, workflowId, stageExecutionId, depth }: NodeProps) {
	const review = reviewMap.get(section.path)
	const isReviewed = !!review
	const isStale = isReviewed && review.contentHash !== section.contentHash

	// Default open if not yet reviewed; once reviewed, collapse. Track manual overrides.
	const [openOverride, setOpenOverride] = useState<boolean | null>(null)
	const open = openOverride !== null ? openOverride : !isReviewed

	const mark = useMarkSectionReviewed(workflowId, stageExecutionId)
	const unmark = useUnmarkSectionReviewed(workflowId, stageExecutionId)

	const handleToggleReview = useCallback(
		(e: React.MouseEvent) => {
			e.stopPropagation()
			if (isReviewed && !isStale) {
				unmark.mutate(section.path)
			} else {
				mark.mutate({ sectionPath: section.path, contentHash: section.contentHash })
				setOpenOverride(false)
			}
		},
		[isReviewed, isStale, section.path, section.contentHash, mark, unmark],
	)

	const isBusy = mark.isPending || unmark.isPending

	const headingColor = isStale ? 'yellow' : isReviewed ? 'green' : undefined

	return (
		<Box
			className={classes.node}
			data-reviewed={isReviewed && !isStale}
			data-stale={isStale}
			style={{ paddingLeft: depth * 16 }}
		>
			{/* Header row */}
			<Group
				gap="xs"
				wrap="nowrap"
				className={classes.header}
				onClick={() => setOpenOverride(o => !(o !== null ? o : !isReviewed))}
			>
				<ActionIcon variant="subtle" size="xs" color="gray" style={{ flexShrink: 0 }}>
					{open ? <VscChevronDown size={12} /> : <VscChevronRight size={12} />}
				</ActionIcon>

				<Text
					size="sm"
					fw={600}
					c={headingColor}
					style={{ flex: 1, userSelect: 'none' }}
				>
					{section.path}. {section.heading}
				</Text>

				{isStale && (
					<Badge size="xs" color="yellow" variant="light">changed</Badge>
				)}

				<Tooltip
					label={
						isStale
							? 'Content changed — re-mark as reviewed'
							: isReviewed
							? 'Reviewed — click to unmark'
							: 'Mark as reviewed'
					}
				>
					<ActionIcon
						size="sm"
						variant={isReviewed ? (isStale ? 'light' : 'filled') : 'subtle'}
						color={isStale ? 'yellow' : isReviewed ? 'green' : 'gray'}
						loading={isBusy}
						onClick={handleToggleReview}
						style={{ flexShrink: 0 }}
					>
						{isStale ? <VscRefresh size={14} /> : <VscCheck size={14} />}
					</ActionIcon>
				</Tooltip>
			</Group>

			{/* Body + children */}
			<Collapse in={open}>
				<Box className={classes.body}>
					{section.body.trim() && (
						<Box className={classes.markdown}>
							<ReactMarkdown remarkPlugins={[remarkGfm]}>
								{section.body}
							</ReactMarkdown>
						</Box>
					)}
					{section.children.map(child => (
						<SectionNode
							key={child.path}
							section={child}
							reviewMap={reviewMap}
							workflowId={workflowId}
							stageExecutionId={stageExecutionId}
							depth={depth + 1}
						/>
					))}
				</Box>
			</Collapse>
		</Box>
	)
}
