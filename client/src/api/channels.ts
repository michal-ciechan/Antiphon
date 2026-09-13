import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiGet, apiPatch } from './client'

export type ChatChannelKind = 'Direct' | 'Group' | 'Broadcast'
export type AlertSeverity = 'Info' | 'Warning' | 'Error' | 'Critical'

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
  lastReplyAt: string | null
  lastReplyPreview: string | null
  messageCount: number
  createdAt: string
  /** Non-null = this channel is an alert sink for severities >= the value. */
  alertMinSeverity: AlertSeverity | null
  digestEnabled: boolean
  digestLastSentAt: string | null
  outboundAgentProfile: string | null
  outboundPreview: ChannelOutboundProfilePreview | null
}

export interface ChannelOutboundProfilePreview {
  projectId: string
  agentId: string
  agentName: string | null
  promptRevision: string
  trigger: string
  timeoutSeconds: number
  authorizationNote: string
}

export interface ChannelOutboundProfileListItem {
  name: string
  projectId: string
  agentId: string
  agentName: string | null
  trigger: string
  timeoutSeconds: number
  maxPending: number
}

export interface UpdateChatChannelRequest {
  agentId?: string | null
  unbindAgent?: boolean
  enabled?: boolean
  alertMinSeverity?: AlertSeverity | null
  clearAlertMinSeverity?: boolean
  digestEnabled?: boolean
  outboundAgentProfile?: string | null
  clearOutboundAgentProfile?: boolean
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

export function useOutboundProfiles() {
  return useQuery({
    queryKey: [...channelKeys.all, 'outbound-profiles'] as const,
    queryFn: () => apiGet<ChannelOutboundProfileListItem[]>('/channels/outbound-profiles'),
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
