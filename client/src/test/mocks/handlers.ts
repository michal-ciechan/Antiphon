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
        'grok-4.6',
        'gpt-5.6-sol',
        'gpt-5.6-terra',
        'gpt-5.6-luna',
      ],
    }),
  ),
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
