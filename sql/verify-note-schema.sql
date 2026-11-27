-- Run in the target LinkShortener database with database-level VIEW DEFINITION.
SET NOCOUNT ON;

DECLARE @notes int = OBJECT_ID(N'dbo.Notes', N'U'),
        @views int = OBJECT_ID(N'dbo.NoteViews', N'U'),
        @headers int = OBJECT_ID(N'dbo.NoteViewRequestHeaders', N'U');

IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'), 0) <> 1
    THROW 51100, 'Run the note schema preflight with database-level VIEW DEFINITION.', 1;

IF @notes IS NULL OR @views IS NULL OR @headers IS NULL
    THROW 51101, 'The target database is missing one or more note tables.', 1;

SELECT OBJECT_NAME(c.object_id) AS TableName, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS DataType,
       c.max_length AS MaxBytes, c.is_nullable AS IsNullable
FROM sys.columns AS c
WHERE c.object_id IN (@notes, @views, @headers)
ORDER BY TableName, c.column_id;

IF EXISTS (
    SELECT 1
    FROM (VALUES (@notes, N'ID', 64), (@views, N'NoteID', 64), (@headers, N'NoteId', 64)) AS expected (TableId, ColumnName, MinChars)
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = expected.TableId AND c.name = expected.ColumnName AND c.is_nullable = 0
          AND (
              (c.system_type_id = TYPE_ID(N'varchar') AND (c.max_length = -1 OR c.max_length >= expected.MinChars))
              OR (expected.TableId <> @notes AND c.system_type_id = TYPE_ID(N'nvarchar')
                  AND (c.max_length = -1 OR c.max_length >= 2 * expected.MinChars))
          )
    )
)
    THROW 51102, 'Note identifier columns must be compatible non-null character columns with room for 64 characters.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = @notes AND i.is_primary_key = 1 AND i.ignore_dup_key = 0 AND i.is_disabled = 0
      AND ic.key_ordinal = 1 AND c.name = N'ID'
      AND NOT EXISTS (
           SELECT 1 FROM sys.index_columns AS other
           WHERE other.object_id = i.object_id AND other.index_id = i.index_id AND other.key_ordinal > 1
      )
)
    THROW 51103, 'dbo.Notes must have an enabled primary key on ID alone with IGNORE_DUP_KEY OFF.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
        (@notes, N'Creator', N'varchar', 0),
        (@notes, N'CreationDate', N'datetime2', 8),
        (@notes, N'NoteText', N'varchar', -1),
        (@views, N'ViewerIP', N'varchar', 45),
        (@views, N'ViewTime', N'datetime2', 8),
        (@headers, N'Name', N'nvarchar', 28),
        (@headers, N'Value', N'nvarchar', 22)
    ) AS expected (TableId, ColumnName, DataType, MinBytes)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = expected.TableId AND c.name = expected.ColumnName
          AND c.system_type_id = TYPE_ID(expected.DataType) AND c.is_nullable = 0
          AND (c.max_length = -1 OR (expected.MinBytes >= 0 AND c.max_length >= expected.MinBytes))
    )
)
    THROW 51104, 'Note text, timestamps, creator, or audit fields differ from the supported schema.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.foreign_keys AS fk
    WHERE fk.is_disabled = 0 AND (
        fk.parent_object_id IN (@notes, @views, @headers) OR fk.referenced_object_id IN (@notes, @views, @headers)
    )
)
    THROW 51105, 'Review the enabled note foreign keys before deploying consumption or cleanup.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.triggers AS t
    WHERE t.is_disabled = 0 AND t.parent_id IN (@notes, @views, @headers)
)
    THROW 51106, 'Review the enabled note triggers before deploying consumption or cleanup.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints AS c
    WHERE c.is_disabled = 0 AND c.parent_object_id IN (@notes, @views, @headers)
)
    THROW 51107, 'Review the enabled note check constraints before deploying consumption or cleanup.', 1;

PRINT 'One-time note schema preflight passed. Identifier columns and the primary key require no migration.';
