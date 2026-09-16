BEGIN READ ONLY;
SELECT now() AS observed_at;
SELECT workspace_id,path,enabled,script_path,schedule,timezone,on_failure FROM schedule WHERE path ILIKE '%antiphon%' OR path ILIKE '%claude_session_cleanup%';
SELECT workspace_id,path,archived,deleted,tag,timeout FROM script WHERE path ILIKE '%antiphon_nightly%';
SELECT j.workspace_id,j.id,j.runnable_path,j.created_at,c.status,c.completed_at FROM v2_job j LEFT JOIN v2_job_completed c USING(id) WHERE j.runnable_path ILIKE '%antiphon_nightly%' OR j.runnable_path ILIKE '%antiphon_build_junk_cleanup%' ORDER BY j.created_at DESC LIMIT 16;
COMMIT;
