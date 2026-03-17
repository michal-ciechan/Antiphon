import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiPost } from './client'

// --- Gate action types ---

export interface GatePromptRequest {
  feedback: string
}

export interface GateCommentRequest {
  content: string
}

// --- Gate mutation hooks ---

export function useApproveGate(workflowId: string | undefined) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () =>
      apiPost<void>(`/workflows/${workflowId}/gates/approve`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['workflows'] })
    },
  })
}

export function useRejectGate(workflowId: string | undefined) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (feedback: string) =>
      apiPost<void>(`/workflows/${workflowId}/gates/reject`, { feedback }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflow', workflowId] })
      queryClient.invalidateQueries({ queryKey: ['workflows'] })
    },
  })
}

export function usePromptAgent(workflowId: string | undefined) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (feedback: string) =>
      apiPost<void>(`/workflows/${workflowId}/gates/prompt`, { feedback }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflow', workflowId] })
    },
  })
}

export function useAddComment(workflowId: string | undefined) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (content: string) =>
      apiPost<void>(`/workflows/${workflowId}/gates/comment`, { content }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflow', workflowId] })
    },
  })
}
