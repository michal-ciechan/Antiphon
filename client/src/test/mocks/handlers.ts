import { HttpResponse, http, type HttpHandler } from 'msw';
import type { AgentModelLevel } from '../../api/agents';
import type { AgentTaskRole } from '../../api/agentTasks';
import type { RolePolicyDto, RolePolicyRowDto } from '../../api/rolePolicy';

function policyRow(
  role: AgentTaskRole,
  level: AgentModelLevel,
  alias: string,
  escalateTo: AgentModelLevel | null,
  configured = true,
): RolePolicyRowDto {
  return {
    role,
    configured,
    level,
    kind: null,
    alias,
    escalateTo,
    escalateAfterMinutes: null,
    timeoutMinutes: 240,
    stagePin: null,
    effective: { source: 'rolePolicy', agentKind: 'ClaudeCode', modelLevel: level, alias, strength: null },
  };
}

/**
 * CARD-0097 F-2: GET /api/role-policy as the server ships it with no pins — the 14 routable roles
 * in server order. Tests that need another row spread this fixture.
 */
export const ROLE_POLICY_FIXTURE: RolePolicyDto = {
  defaultLevel: 'High',
  defaultTimeoutMinutes: 240,
  roles: [
    policyRow('Investigate', 'High', 'opus', 'Frontier'),
    policyRow('Plan', 'Frontier', 'fable', null),
    policyRow('TestDesign', 'Frontier', 'fable', null),
    policyRow('Code', 'Frontier', 'fable', null),
    policyRow('Mutation', 'Low', 'haiku', 'High'),
    policyRow('Review', 'Frontier', 'fable', null),
    policyRow('Debug', 'High', 'opus', 'Frontier'),
    policyRow('Coverage', 'High', 'opus', null),
    policyRow('Docs', 'Medium', 'sonnet', null),
    policyRow('Commit', 'Medium', 'sonnet', null),
    policyRow('Test', 'Low', 'haiku', 'Medium'),
    policyRow('Deploy', 'Low', 'haiku', null),
    policyRow('Merge', 'High', 'opus', null),
    policyRow('Custom', 'High', 'opus', null, false),
  ],
};

/**
 * Default MSW request handlers.
 * Add shared handlers here that should be available in all tests.
 * Individual tests can override or extend these via server.use().
 */
export const handlers: HttpHandler[] = [
  // CARD-0212: useRemoteControlSupport always GETs runner-types, including on screens
  // that never previously asked. Empty list = Unknown-as-Unsupported.
  http.get('/api/agent-tui/runner-types', () => HttpResponse.json([])),
  http.get('/api/model-availability', () =>
    HttpResponse.json({
      holds: [],
      available: [
        'fable',
        'opus',
        'sonnet',
        'haiku',
        'grok-4.7',
        'gpt-6-astra',
        'gpt-6.1-sol',
        'gpt-5.6-luna',
      ],
    }),
  ),
  http.get('/api/complexity-chains', () =>
    HttpResponse.json({
      chains: [],
      roles: [],
      complexities: ['Hard', 'Medium', 'Easy'],
    }),
  ),
  http.get('/api/routing-pins', () => HttpResponse.json({ pins: [] })),
  http.get('/api/role-policy', () => HttpResponse.json(ROLE_POLICY_FIXTURE)),
  http.get('/api/session-runners', () => HttpResponse.json([
    {
      runnerId: 'desktop',
      displayName: 'Desktop',
      platform: 'windows',
      platformObservedAt: '2026-09-25T00:00:00Z',
      available: true,
      dispatchEligible: true,
      unavailableReason: null,
      capacity: 6,
      occupied: 0,
      capacityKind: 'delegatedTasks',
      capacityObservedAt: '2026-09-25T00:00:00Z',
      stale: false,
      features: ['required-platform-v1'],
    },
    {
      runnerId: 'server2',
      displayName: 'server2',
      platform: 'linux',
      platformObservedAt: '2026-09-25T00:00:00Z',
      available: true,
      dispatchEligible: true,
      unavailableReason: null,
      capacity: 4,
      occupied: 1,
      capacityKind: 'sessions',
      capacityObservedAt: '2026-09-25T00:00:00Z',
      stale: false,
      features: ['required-platform-v1'],
    },
  ])),
  http.get('/api/hosts/stats', () => HttpResponse.json([])),
  http.get('/api/hosts', () => HttpResponse.json([])),
  http.get('/api/hosts/:hostId/stats/series', ({ params, request }) => {
    const query = new URL(request.url).searchParams
    return HttpResponse.json({ hostId: params.hostId, metric: query.get('metric'), window: query.get('window'), intervalSeconds: 5, points: [] })
  }),
  http.get('/api/runner-defaults', () => HttpResponse.json({
    revision: 1,
    globalRunnerId: 'server2',
    kindDefaults: [],
    updatedAt: '2026-09-25T00:00:00Z',
    lastReason: 'Imported Delegation:DefaultRunnerId.',
    lastProvenance: 'Migration',
    lastCallerTaskId: null,
    supportedKinds: ['Grok', 'ClaudeCode', 'Codex'],
    unresolvedReferences: [],
  })),
  http.get('/api/runner-defaults/revisions', () => HttpResponse.json({ revisions: [], nextBeforeRevision: null })),
  http.get('/api/boards', () => HttpResponse.json([])),
  http.get('/api/subscription-usage', () => HttpResponse.json([])),
  // CARD-0255: AgentCreateModal loads the setup catalog for preset chips.
  http.get('/api/projects/setup-catalog', () => HttpResponse.json({
    modelLevels: [],
    replyStyles: [],
    bundles: [
      { key: 'orchestrator', version: '1', stamp: 'orchestrator v1', summary: 'You are an orchestrator.', chars: 10 },
      { key: 'board-api', version: '1', stamp: 'board-api v1', summary: 'Working the Antiphon board.', chars: 10 },
    ],
    profiles: [],
    presets: [
      {
        key: 'orchestrator',
        label: 'Standing orchestrator',
        description: 'Watches the board, delegates every change.',
        alwaysOn: true,
        modelLevel: 'High',
        replyStyle: 'Normal',
        bundleKeys: ['orchestrator', 'board-api'],
        systemPromptTemplate: 'You watch {project} on {board} at {directory}.',
        namePattern: '{project} Orchestrator',
        remoteControlEnabled: true,
        defaultWorkflowTemplateId: 'b0000000-0000-0000-0000-000000000001',
      },
      {
        key: 'worker',
        label: 'Worker',
        description: 'A worker you hand cards or tasks to.',
        alwaysOn: false,
        modelLevel: 'High',
        replyStyle: 'Normal',
        bundleKeys: [],
        systemPromptTemplate: null,
        namePattern: '{project} Worker',
        remoteControlEnabled: false,
        defaultWorkflowTemplateId: null,
      },
    ],
    delegation: {
      allowedRoots: [],
      allowedRootsIsEmpty: true,
      maxConcurrentTasks: 1,
      maxCostUsdPerRoot: 10,
      maxDepth: 2,
      defaultLevel: 'High',
    },
  })),
];
