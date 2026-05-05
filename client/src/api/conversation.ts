import { useQuery } from '@tanstack/react-query'

export interface ConversationEntryDto {
  id: string
  type: 'agent' | 'tool-call'
  content: string
  timestamp: string
  stageId: string | null
  stageName: string
  toolName?: string | null
  toolInput?: string | null
  toolOutput?: string | null
  fullContent?: string | null
}

export function useConversation(workflowId?: string) {
  return useQuery<ConversationEntryDto[]>({
    queryKey: ['conversation', workflowId],
    queryFn: async () => {
      const res = await fetch(`/api/audit/conversation?workflowId=${workflowId}`)
      if (!res.ok) throw new Error('Failed to fetch conversation')
      return res.json()
    },
    enabled: !!workflowId,
    staleTime: 30_000,
  })
}
