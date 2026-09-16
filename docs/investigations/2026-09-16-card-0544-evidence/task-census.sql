BEGIN READ ONLY;
SELECT now() AS observed_at;
SELECT "Id", "Role", "Status", "Title", "DispatchedAt", "CompletedAt", "CostUsd", "AgentSessionId" FROM "AgentTasks" WHERE "CardId"='75f13b1a-649c-4b4b-a034-e1b16663b591' AND "Role" IN (2,3) ORDER BY "CreatedAt";
COMMIT;
