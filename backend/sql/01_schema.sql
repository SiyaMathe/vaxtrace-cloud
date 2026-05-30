-- =============================================================================
-- VaxTrace Cloud — Database Schema
-- Azure SQL Database / SQL Server 2022
-- =============================================================================

USE master;
GO

IF DB_ID('VaxTraceDB') IS NOT NULL
BEGIN
    ALTER DATABASE VaxTraceDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE VaxTraceDB;
END
GO

CREATE DATABASE VaxTraceDB COLLATE SQL_Latin1_General_CP1_CI_AS;
GO

USE VaxTraceDB;
GO

-- =============================================================================
-- LOOKUP TABLES
-- =============================================================================

-- Vaccine manufacturers / products
CREATE TABLE dbo.Vaccine (
    VaccineID       INT             NOT NULL IDENTITY(1,1),
    VaccineName     NVARCHAR(200)   NOT NULL,
    Manufacturer    NVARCHAR(200)   NOT NULL,
    DosesRequired   TINYINT         NOT NULL DEFAULT 2,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CONSTRAINT PK_Vaccine       PRIMARY KEY (VaccineID),
    CONSTRAINT UQ_Vaccine_Name  UNIQUE (VaccineName)
);
GO

-- Vaccination centres (providers)
CREATE TABLE dbo.VaccinationCenter (
    CenterID        INT             NOT NULL IDENTITY(1,1),
    CenterName      NVARCHAR(300)   NOT NULL,
    Province        NVARCHAR(100)   NULL,
    City            NVARCHAR(100)   NULL,
    IsActive        BIT             NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_VaccinationCenter PRIMARY KEY (CenterID)
);
GO

-- =============================================================================
-- CORE TABLES
-- =============================================================================

-- One row per person — identified by SA ID or passport number
CREATE TABLE dbo.Person (
    PersonID        INT             NOT NULL IDENTITY(1,1),
    IDNumber        VARCHAR(20)     NOT NULL,   -- SA ID or passport number
    IDType          VARCHAR(20)     NOT NULL DEFAULT 'SA_ID',
    FirstName       NVARCHAR(100)   NULL,
    LastName        NVARCHAR(100)   NULL,
    DateOfBirth     DATE            NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Person        PRIMARY KEY (PersonID),
    CONSTRAINT UQ_Person_ID     UNIQUE (IDNumber),
    CONSTRAINT CK_Person_IDType CHECK (IDType IN ('SA_ID', 'PASSPORT', 'FOREIGN_ID'))
);
GO

-- One row per vaccination event (a person can have multiple doses)
CREATE TABLE dbo.VaccinationRecord (
    RecordID            INT             NOT NULL IDENTITY(1,1),
    PersonID            INT             NOT NULL,
    CenterID            INT             NULL,       -- nullable: centre may not exist yet in lookup
    CenterNameRaw       NVARCHAR(300)   NULL,       -- raw string from provider message
    VaccineID           INT             NULL,
    VaccineSerialNumber VARCHAR(100)    NULL,
    VaccineBarcode      VARCHAR(100)    NULL,
    DoseNumber          TINYINT         NOT NULL DEFAULT 1,
    VaccinationDate     DATE            NOT NULL,
    MessageFormat       CHAR(1)         NOT NULL,   -- 'A' or 'B' — which provider format
    RawMessage          NVARCHAR(500)   NULL,       -- original queue message (full audit)
    BlobPath            NVARCHAR(500)   NULL,       -- path in Blob Storage archive
    IsVerified          BIT             NOT NULL DEFAULT 0,
    ProcessedAt         DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_VaccinationRecord         PRIMARY KEY (RecordID),
    CONSTRAINT FK_VacRecord_Person          FOREIGN KEY (PersonID)  REFERENCES dbo.Person(PersonID),
    CONSTRAINT FK_VacRecord_Center          FOREIGN KEY (CenterID)  REFERENCES dbo.VaccinationCenter(CenterID),
    CONSTRAINT FK_VacRecord_Vaccine         FOREIGN KEY (VaccineID) REFERENCES dbo.Vaccine(VaccineID),
    CONSTRAINT CK_VacRecord_MessageFormat   CHECK (MessageFormat IN ('A','B','U')),
    CONSTRAINT CK_VacRecord_DoseNumber      CHECK (DoseNumber BETWEEN 1 AND 5)
);
GO

-- Queue message processing log (audit trail of every message received)
CREATE TABLE dbo.QueueMessageLog (
    LogID           INT             NOT NULL IDENTITY(1,1),
    MessageID       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    RawMessage      NVARCHAR(1000)  NOT NULL,
    MessageFormat   CHAR(1)         NULL,
    ParsedIDNumber  VARCHAR(20)     NULL,
    Status          VARCHAR(20)     NOT NULL DEFAULT 'RECEIVED',
    ErrorMessage    NVARCHAR(500)   NULL,
    RecordID        INT             NULL,    -- FK to VaccinationRecord if successful
    ReceivedAt      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedAt     DATETIME2       NULL,
    CONSTRAINT PK_QueueMessageLog   PRIMARY KEY (LogID),
    CONSTRAINT CK_QueueLog_Status   CHECK (Status IN ('RECEIVED','PROCESSING','SUCCESS','FAILED','DUPLICATE'))
);
GO

-- =============================================================================
-- INDEXES
-- =============================================================================

-- Person lookup by ID number (the primary search path — must be sub-second)
CREATE UNIQUE NONCLUSTERED INDEX IX_Person_IDNumber
    ON dbo.Person (IDNumber);
GO

-- Vaccination records by person (order history)
CREATE NONCLUSTERED INDEX IX_VacRecord_PersonID_Date
    ON dbo.VaccinationRecord (PersonID, VaccinationDate DESC)
    INCLUDE (DoseNumber, CenterNameRaw, VaccineSerialNumber, IsVerified);
GO

-- Queue log monitoring (failed messages dashboard)
CREATE NONCLUSTERED INDEX IX_QueueLog_Status_Received
    ON dbo.QueueMessageLog (Status, ReceivedAt DESC)
    WHERE Status IN ('FAILED', 'DUPLICATE');
GO

-- Recent processing for the stats endpoint
CREATE NONCLUSTERED INDEX IX_QueueLog_ReceivedAt
    ON dbo.QueueMessageLog (ReceivedAt DESC)
    INCLUDE (Status, MessageFormat);
GO

PRINT 'VaxTraceDB schema created successfully.';
GO
