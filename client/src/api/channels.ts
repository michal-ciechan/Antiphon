import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPatch } from './client'

export type ChatChannelKind = 'Direct' | 'Group' | 'Broadcast'

export interface ChatChannelDto {
  id: string
  provider: string
  externalId: string
  kind: ChatChannelKind
  title: string | null
  agentId: string | null
  agentName: string | null
  enabled: boolean
  lastMessageAt: string | null
  lastMessagePreview: string | null
  lastAuthor: string | null
  messageCount: number
  createdAt: string
}

export interface UpdateChatChannelRequest {
  agentId?: string | null
  unbindAgent?: boolean
  enabled?: boolean
}

export const channelKeys = {
  all: ['channels'] as const,
}

export function useChannels() {
  return useQuery({
    queryKey: channelKeys.all,
    queryFn: () => apiGet<ChatChannelDto[]>('/channels'),
    staleTime: 10_000,
  })
}

export function useUpdateChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdateChatChannelRequest }) =>
      apiPatch<ChatChannelDto>(`/channels/${id}`, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: channelKeys.all })
    },
  })
}
