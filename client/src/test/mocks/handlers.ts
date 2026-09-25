import { HttpResponse, http, type HttpHandler } from 'msw';

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
        'gpt-6-sol',
        'gpt-5.6-terra',
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
