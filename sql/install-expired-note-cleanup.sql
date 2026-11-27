-- Run as a SQL Server sysadmin after verify-note-schema.sql passes in LinkShortener.
USE msdb;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @jobName sysname = N'LinkShortener - Expired Note Cleanup',
        @stepName sysname = N'Delete expired notes',
        @scheduleName sysname = N'LinkShortener - Expired Note Cleanup (Hourly)',
        @jobId uniqueidentifier,
        @stepId int,
        @scheduleId int,
        @lockResult int,
        @command nvarchar(max) = N'DELETE FROM dbo.Notes
WHERE CreationDate <= DATEADD(DAY, -30, GETDATE());';

IF OBJECT_ID(N'LinkShortener.dbo.Notes', N'U') IS NULL
    THROW 51200, 'LinkShortener.dbo.Notes was not found on this SQL Server.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    EXEC @lockResult = dbo.sp_getapplock @Resource = @jobName, @LockMode = 'Exclusive', @LockTimeout = 10000;
    IF @lockResult < 0
        THROW 51201, 'Could not acquire the expired-note job installation lock.', 1;

    SELECT @jobId = job_id FROM dbo.sysjobs WHERE name = @jobName;
    IF @jobId IS NULL
        EXEC dbo.sp_add_job @job_name = @jobName, @enabled = 1,
            @description = N'Permanently removes LinkShortener notes that are at least 30 days old.',
            @job_id = @jobId OUTPUT;

    SELECT @stepId = step_id FROM dbo.sysjobsteps WHERE job_id = @jobId AND step_name = @stepName;
    IF @stepId IS NULL
    BEGIN
        EXEC dbo.sp_add_jobstep @job_id = @jobId, @step_name = @stepName, @subsystem = N'TSQL',
            @database_name = N'LinkShortener', @command = @command, @on_success_action = 1, @on_fail_action = 2;
        SELECT @stepId = step_id FROM dbo.sysjobsteps WHERE job_id = @jobId AND step_name = @stepName;
    END
    ELSE
        EXEC dbo.sp_update_jobstep @job_id = @jobId, @step_id = @stepId, @subsystem = N'TSQL',
            @database_name = N'LinkShortener', @command = @command, @on_success_action = 1, @on_fail_action = 2;

    EXEC dbo.sp_update_job @job_id = @jobId, @enabled = 1, @start_step_id = @stepId;

    IF (SELECT COUNT(*) FROM dbo.sysschedules WHERE name = @scheduleName) > 1
        THROW 51202, 'Multiple schedules have the expected cleanup schedule name. Review them before continuing.', 1;

    SELECT @scheduleId = schedule_id FROM dbo.sysschedules WHERE name = @scheduleName;
    IF EXISTS (
        SELECT 1 FROM dbo.sysjobschedules
        WHERE (schedule_id = @scheduleId AND job_id <> @jobId)
           OR (job_id = @jobId AND (@scheduleId IS NULL OR schedule_id <> @scheduleId))
    )
        THROW 51203, 'The cleanup job has an unexpected schedule, or its schedule belongs to another job.', 1;

    IF @scheduleId IS NULL
        EXEC dbo.sp_add_schedule @schedule_name = @scheduleName, @enabled = 1,
            @freq_type = 4, @freq_interval = 1, @freq_subday_type = 8, @freq_subday_interval = 1,
            @active_start_date = 20000101, @active_end_date = 99991231,
            @active_start_time = 0, @active_end_time = 235959, @schedule_id = @scheduleId OUTPUT;
    ELSE
        EXEC dbo.sp_update_schedule @schedule_id = @scheduleId, @enabled = 1,
            @freq_type = 4, @freq_interval = 1, @freq_subday_type = 8, @freq_subday_interval = 1,
            @active_start_date = 20000101, @active_end_date = 99991231,
            @active_start_time = 0, @active_end_time = 235959;

    IF NOT EXISTS (SELECT 1 FROM dbo.sysjobschedules WHERE job_id = @jobId AND schedule_id = @scheduleId)
        EXEC dbo.sp_attach_schedule @job_id = @jobId, @schedule_id = @scheduleId;

    IF NOT EXISTS (SELECT 1 FROM dbo.sysjobservers WHERE job_id = @jobId AND server_id = 0)
        EXEC dbo.sp_add_jobserver @job_id = @jobId, @server_name = N'(LOCAL)';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
