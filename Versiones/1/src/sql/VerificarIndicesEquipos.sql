USE EndpointManager;
GO

IF OBJECT_ID('dbo.Equipos', 'U') IS NULL
BEGIN
    THROW 50001, 'La tabla dbo.Equipos no existe.', 1;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Equipos_Guid'
      AND object_id = OBJECT_ID('dbo.Equipos')
)
BEGIN
    CREATE UNIQUE INDEX IX_Equipos_Guid ON dbo.Equipos(Guid);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Equipos_Heartbeat'
      AND object_id = OBJECT_ID('dbo.Equipos')
)
BEGIN
    CREATE INDEX IX_Equipos_Heartbeat ON dbo.Equipos(UltimoHeartbeat);
END;
GO
