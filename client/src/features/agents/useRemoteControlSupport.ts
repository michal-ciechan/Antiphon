import {
  remoteControlCapability,
  useAgentTuiProfiles,
  useAgentTuiRunnerTypes,
  type AgentKind,
} from '../../api/agentTui'

/**
 * CARD-0212. Resolves the catalog's remoteControl row for the runner an agent would launch:
 * the selected/attached profile first, the kind's runner type when there is no profile,
 * undefined while neither has loaded. `supported` is true ONLY on a Supported row — an
 * unloaded or Unknown row disables the control, mirroring the server's Unknown-is-Unsupported.
 */
export function useRemoteControlSupport(input: {
  tuiProfileId?: string | null
  kind?: AgentKind | null
}): { supported: boolean; reason: string | undefined; resolved: boolean } {
  const profiles = useAgentTuiProfiles()
  const runnerTypes = useAgentTuiRunnerTypes()

  const profile = input.tuiProfileId
    ? profiles.data?.find((candidate) => candidate.id === input.tuiProfileId)
    : undefined
  const runnerType = input.kind
    ? runnerTypes.data?.find((candidate) => candidate.kind === input.kind)
    : undefined

  const capability = remoteControlCapability(profile) ?? remoteControlCapability(runnerType)
  if (capability) {
    return {
      supported: capability.state === 'Supported',
      reason: capability.reason,
      resolved: true,
    }
  }

  const waitingOnProfile = !!input.tuiProfileId && (profiles.isLoading || profiles.isFetching)
  const waitingOnKind = !profile && !!input.kind && (runnerTypes.isLoading || runnerTypes.isFetching)
  if (waitingOnProfile || waitingOnKind) {
    return { supported: false, reason: undefined, resolved: false }
  }

  return { supported: false, reason: undefined, resolved: true }
}
