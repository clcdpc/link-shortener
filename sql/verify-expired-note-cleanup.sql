USE msdb;
GO

SET NOCOUNT ON;

DECLARE @jobName sysname = N'LinkShortener - Expired Note Cleanup',
        @scheduleName sysname = N'LinkShortener - Expired Note Cleanup (Hourly)',
        @jobId uniqueidentifier,
        @scheduleId int;

SELECT @jobId = job_id FROM dbo.sysjobs WHERE name = @jobName AND enabled = 1;
SELECT @scheduleId = schedule_id FROM dbo.sysschedules
WHERE name = @scheduleName AND enabled = 1 AND freq_type = 4 AND freq_interval = 1
  AND freq_subday_type = 8 AND freq_subday_interval = 1 AND active_start_time = 0 AND active_end_time = 235959;

IF @jobId IS NULL OR @scheduleId IS NULL
    THROW 51210, 'The enabled cleanup job and hourly schedule were not found.', 1;

IF (SELECT COUNT(*) FROM dbo.sysschedules WHERE name = @scheduleName) <> 1
   OR (SELECT COUNT(*) FROM dbo.sysjobschedules WHERE job_id = @jobId) <> 1
   OR (SELECT COUNT(*) FROM dbo.sysjobschedules WHERE schedule_id = @scheduleId) <> 1
   OR NOT EXISTS (SELECT 1 FROM dbo.sysjobschedules WHERE job_id = @jobId AND schedule_id = @scheduleId)
   OR NOT EXISTS (SELECT 1 FROM dbo.sysjobservers WHERE job_id = @jobId AND server_id = 0)
    THROW 51211, 'The cleanup job must have exactly one dedicated schedule and a local target.', 1;

IF NOT EXISTS (
    SELECT 1 FROM dbo.sysjobsteps AS s
    JOIN dbo.sysjobs AS j ON s.job_id = j.job_id AND s.step_id = j.start_step_id
    WHERE s.job_id = @jobId AND s.step_name = N'Delete expired notes' AND s.subsystem = N'TSQL'
      AND s.database_name = N'LinkShortener'
      AND REPLACE(s.command, CHAR(13), N'') =
          N'DELETE FROM dbo.Notes' + CHAR(10) + N'WHERE CreationDate <= DATEADD(DAY, -30, GETDATE());'
      AND s.on_success_action = 1 AND s.on_fail_action = 2
)
    THROW 51212, 'The cleanup job entry step does not match the expected deletion.', 1;

PRINT 'Expired note cleanup job, local target, and hourly schedule verified.';
