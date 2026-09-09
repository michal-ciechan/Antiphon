import { Input, SegmentedControl } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { AGENT_REPLY_STYLE_OPTIONS, type AgentReplyStyle } from '../../api/agents'

/** Shared reply-style picker so agent setup surfaces present the same choices and descriptions. */
export function ReplyStyleControl({
  value,
  onChange,
}: {
  value: AgentReplyStyle
  onChange: (value: AgentReplyStyle) => void
}) {
  const narrow = useMediaQuery('(max-width: 36em)')

  return (
    <Input.Wrapper
      label="Reply style"
      description={AGENT_REPLY_STYLE_OPTIONS.find((option) => option.value === value)?.description ?? ''}
    >
      <SegmentedControl
        fullWidth
        orientation={narrow ? 'vertical' : 'horizontal'}
        mt={4}
        data={AGENT_REPLY_STYLE_OPTIONS.map(({ value: optionValue, label }) => ({ value: optionValue, label }))}
        value={value}
        onChange={(next) => onChange(next as AgentReplyStyle)}
      />
    </Input.Wrapper>
  )
}
