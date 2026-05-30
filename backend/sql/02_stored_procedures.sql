-- =============================================================================
-- VaxTrace Cloud — Stored Procedures
-- =============================================================================

USE VaxTraceDB;
GO

-- =============================================================================
-- SP 1: Upsert a vaccination record from a parsed queue message
--       Idempotent — safe to re-process the same message (deduplication via
--       VaccineSerialNumber + PersonID + VaccinationDate)
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_UpsertVaccinationRecord
    @IDNumber           VARCHAR(20),
    @IDType             VARCHAR(20)     = 'SA_ID',
    @CenterNameRaw      NVARCHAR(300),
    @VaccineSerialNumber VARCHAR(100)   = NULL,
    @VaccineBarcode     VARCHAR(100)    = NULL,
    @VaccinationDate    DATE,
    @MessageFormat      CHAR(1),
    @RawMessage         NVARCHAR(500),
    @BlobPath           NVARCHAR(500)   = NULL,
    @RecordID           INT             OUTPUT,
    @IsNew              BIT             OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @PersonID   INT;
    DECLARE @CenterID   INT;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- ── Get or create Person ─────────────────────────────────────────────
        SELECT @PersonID = PersonID
        FROM   dbo.Person
        WHERE  IDNumber = @IDNumber;

        IF @PersonID IS NULL
        BEGIN
            INSERT INTO dbo.Person (IDNumber, IDType)
            VALUES (@IDNumber, @IDType);
            SET @PersonID = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.Person SET UpdatedAt = SYSUTCDATETIME()
            WHERE PersonID = @PersonID;
        END

        -- ── Match or create VaccinationCenter ────────────────────────────────
        SELECT @CenterID = CenterID
        FROM   dbo.VaccinationCenter
        WHERE  CenterName = @CenterNameRaw AND IsActive = 1;

        IF @CenterID IS NULL AND @CenterNameRaw IS NOT NULL
        BEGIN
            INSERT INTO dbo.VaccinationCenter (CenterName)
            VALUES (@CenterNameRaw);
            SET @CenterID = SCOPE_IDENTITY();
        END

        -- ── Check for duplicate record ───────────────────────────────────────
        SELECT @RecordID = RecordID
        FROM   dbo.VaccinationRecord
        WHERE  PersonID             = @PersonID
          AND  VaccinationDate      = @VaccinationDate
          AND  (
                   (@VaccineSerialNumber IS NOT NULL AND VaccineSerialNumber = @VaccineSerialNumber)
                OR (@VaccineBarcode      IS NOT NULL AND VaccineBarcode      = @VaccineBarcode)
               );

        IF @RecordID IS NOT NULL
        BEGIN
            -- Already exists — update blob path if provided, return existing ID
            IF @BlobPath IS NOT NULL
                UPDATE dbo.VaccinationRecord
                SET BlobPath = @BlobPath
                WHERE RecordID = @RecordID;

            SET @IsNew = 0;
            COMMIT TRANSACTION;
            RETURN;
        END

        -- ── Calculate dose number for this person ────────────────────────────
        DECLARE @DoseNumber TINYINT;
        SELECT @DoseNumber = ISNULL(MAX(DoseNumber), 0) + 1
        FROM   dbo.VaccinationRecord
        WHERE  PersonID = @PersonID;

        -- ── Insert new vaccination record ────────────────────────────────────
        INSERT INTO dbo.VaccinationRecord (
            PersonID, CenterID, CenterNameRaw,
            VaccineSerialNumber, VaccineBarcode,
            DoseNumber, VaccinationDate,
            MessageFormat, RawMessage, BlobPath
        )
        VALUES (
            @PersonID, @CenterID, @CenterNameRaw,
            @VaccineSerialNumber, @VaccineBarcode,
            @DoseNumber, @VaccinationDate,
            @MessageFormat, @RawMessage, @BlobPath
        );

        SET @RecordID = SCOPE_IDENTITY();
        SET @IsNew    = 1;

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- =============================================================================
-- SP 2: Query vaccination status by ID number
--       Returns all doses for the person — used by the HTTP query function
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_GetVaccinationStatus
    @IDNumber   VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    -- Person summary
    SELECT
        p.PersonID,
        p.IDNumber,
        p.IDType,
        p.FirstName,
        p.LastName,
        COUNT(vr.RecordID)                          AS TotalDoses,
        MIN(vr.VaccinationDate)                     AS FirstDoseDate,
        MAX(vr.VaccinationDate)                     AS LatestDoseDate,
        -- Fully vaccinated = 2+ doses
        CASE WHEN COUNT(vr.RecordID) >= 2 THEN 1 ELSE 0 END AS IsFullyVaccinated,
        -- Days since last dose
        DATEDIFF(DAY, MAX(vr.VaccinationDate), CAST(SYSUTCDATETIME() AS DATE)) AS DaysSinceLastDose
    FROM       dbo.Person             p
    LEFT JOIN  dbo.VaccinationRecord  vr ON vr.PersonID = p.PersonID
    WHERE      p.IDNumber = @IDNumber
    GROUP BY   p.PersonID, p.IDNumber, p.IDType, p.FirstName, p.LastName;

    -- Individual dose records
    SELECT
        vr.RecordID,
        vr.DoseNumber,
        vr.VaccinationDate,
        vr.CenterNameRaw        AS VaccinationCenter,
        vr.VaccineSerialNumber,
        vr.VaccineBarcode,
        vr.MessageFormat        AS ProviderFormat,
        vr.IsVerified,
        vr.ProcessedAt
    FROM       dbo.VaccinationRecord  vr
    JOIN       dbo.Person             p  ON p.PersonID = vr.PersonID
    WHERE      p.IDNumber = @IDNumber
    ORDER BY   vr.DoseNumber;
END;
GO

-- =============================================================================
-- SP 3: Log queue message (called at start of processing)
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_LogQueueMessage
    @RawMessage     NVARCHAR(1000),
    @MessageFormat  CHAR(1)         = NULL,
    @ParsedIDNumber VARCHAR(20)     = NULL,
    @LogID          INT             OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.QueueMessageLog (RawMessage, MessageFormat, ParsedIDNumber, Status)
    VALUES (@RawMessage, @MessageFormat, @ParsedIDNumber, 'RECEIVED');

    SET @LogID = SCOPE_IDENTITY();
END;
GO

-- =============================================================================
-- SP 4: Update queue message log after processing
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_UpdateQueueMessageLog
    @LogID          INT,
    @Status         VARCHAR(20),
    @RecordID       INT             = NULL,
    @ErrorMessage   NVARCHAR(500)   = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.QueueMessageLog
    SET
        Status       = @Status,
        RecordID     = @RecordID,
        ErrorMessage = @ErrorMessage,
        ProcessedAt  = SYSUTCDATETIME()
    WHERE LogID = @LogID;
END;
GO

-- =============================================================================
-- SP 5: Processing stats — used by the /api/vaccination/stats endpoint
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_GetProcessingStats
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*)                                                        AS TotalMessagesReceived,
        SUM(CASE WHEN Status = 'SUCCESS'    THEN 1 ELSE 0 END)         AS Successful,
        SUM(CASE WHEN Status = 'FAILED'     THEN 1 ELSE 0 END)         AS Failed,
        SUM(CASE WHEN Status = 'DUPLICATE'  THEN 1 ELSE 0 END)         AS Duplicates,
        SUM(CASE WHEN Status IN ('RECEIVED','PROCESSING') THEN 1 ELSE 0 END) AS Pending,
        SUM(CASE WHEN MessageFormat = 'A'   THEN 1 ELSE 0 END)         AS FormatA_Count,
        SUM(CASE WHEN MessageFormat = 'B'   THEN 1 ELSE 0 END)         AS FormatB_Count,
        MIN(ReceivedAt)                                                 AS EarliestMessage,
        MAX(ReceivedAt)                                                 AS LatestMessage
    FROM dbo.QueueMessageLog
    WHERE ReceivedAt >= DATEADD(DAY, -1, SYSUTCDATETIME());  -- last 24 hours

    -- Recent failures (for alerting)
    SELECT TOP 10
        LogID, RawMessage, ErrorMessage, ReceivedAt
    FROM dbo.QueueMessageLog
    WHERE Status      = 'FAILED'
      AND ReceivedAt >= DATEADD(HOUR, -1, SYSUTCDATETIME())
    ORDER BY ReceivedAt DESC;

    -- Person + dose counts
    SELECT
        COUNT(DISTINCT p.PersonID)                                          AS TotalPeople,
        COUNT(vr.RecordID)                                                  AS TotalDoses,
        SUM(CASE WHEN dos.DoseCount >= 2 THEN 1 ELSE 0 END)                AS FullyVaccinated,
        SUM(CASE WHEN dos.DoseCount  = 1 THEN 1 ELSE 0 END)                AS PartiallyVaccinated
    FROM dbo.Person p
    LEFT JOIN dbo.VaccinationRecord vr ON vr.PersonID = p.PersonID
    LEFT JOIN (
        SELECT PersonID, COUNT(*) AS DoseCount
        FROM   dbo.VaccinationRecord
        GROUP BY PersonID
    ) dos ON dos.PersonID = p.PersonID;
END;
GO

PRINT 'VaxTrace stored procedures created.';
GO
