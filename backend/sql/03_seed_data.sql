-- =============================================================================
-- VaxTrace Cloud — Seed Data
-- Hard-coded valid ID numbers (as per CLDV6212 POE Part 1 requirement)
-- These are test SA ID numbers for development purposes only
-- =============================================================================

USE VaxTraceDB;
GO

-- Seed vaccine types
INSERT INTO dbo.Vaccine (VaccineName, Manufacturer, DosesRequired) VALUES
    ('Pfizer-BioNTech (Comirnaty)',   'Pfizer',       2),
    ('Johnson & Johnson (Janssen)',    'Johnson',      1),
    ('Oxford-AstraZeneca (Vaxzevria)','AstraZeneca',  2),
    ('Moderna (Spikevax)',            'Moderna',      2);
GO

-- Seed vaccination centres
INSERT INTO dbo.VaccinationCenter (CenterName, Province, City) VALUES
    ('Groote Schuur Hospital',          'Western Cape',   'Cape Town'),
    ('Charlotte Maxeke Hospital',       'Gauteng',        'Johannesburg'),
    ('Inkosi Albert Luthuli Hospital',  'KwaZulu-Natal',  'Durban'),
    ('Tygerberg Hospital',              'Western Cape',   'Bellville'),
    ('Steve Biko Academic Hospital',    'Gauteng',        'Pretoria'),
    ('Mediclinic Sandton',              'Gauteng',        'Sandton'),
    ('Life Hilton Private Hospital',    'KwaZulu-Natal',  'Pietermaritzburg'),
    ('Netcare Blaauwberg Hospital',     'Western Cape',   'Bloubergstrand');
GO

-- =============================================================================
-- Hard-coded valid test IDs (per POE requirement: "hard code some valid ID numbers")
-- Format: 13-digit SA ID numbers for fictitious test persons
-- =============================================================================

-- Insert test persons
INSERT INTO dbo.Person (IDNumber, IDType, FirstName, LastName, DateOfBirth) VALUES
    ('0105215359081', 'SA_ID', 'Siyabulela', 'Mathe',   '2001-05-21'),
    ('8001015009087', 'SA_ID', 'Thabo',      'Nkosi',   '1980-01-01'),
    ('9203224800088', 'SA_ID', 'Ayanda',     'Dube',    '1992-03-22'),
    ('7512086150082', 'SA_ID', 'Lerato',     'Molefe',  '1975-12-08'),
    ('0407145189089', 'SA_ID', 'Amahle',     'Zulu',    '2004-07-14'),
    ('P12345678',     'PASSPORT', 'James',   'Smith',   NULL);
GO

-- Insert vaccination records (multiple doses, different formats)
DECLARE @PersonID INT;

-- Person 1: 2 doses (Pfizer) — fully vaccinated
SELECT @PersonID = PersonID FROM dbo.Person WHERE IDNumber = '0105215359081';
INSERT INTO dbo.VaccinationRecord (
    PersonID, CenterID, CenterNameRaw, VaccineID, VaccineSerialNumber,
    DoseNumber, VaccinationDate, MessageFormat, RawMessage, IsVerified
) VALUES
(
    @PersonID, 1, 'Groote Schuur Hospital', 1, 'PFZ-2024-001-A',
    1, '2024-01-15', 'A',
    '0105215359081:Groote Schuur Hospital:2024-01-15:PFZ-2024-001-A', 1
),
(
    @PersonID, 1, 'Groote Schuur Hospital', 1, 'PFZ-2024-001-B',
    2, '2024-02-12', 'B',
    'BAR-00001:2024-02-12:Groote Schuur Hospital:0105215359081', 1
);

-- Person 2: 1 dose (J&J) — partially vaccinated (J&J needs only 1, so mark as verified)
SELECT @PersonID = PersonID FROM dbo.Person WHERE IDNumber = '8001015009087';
INSERT INTO dbo.VaccinationRecord (
    PersonID, CenterID, CenterNameRaw, VaccineID, VaccineSerialNumber,
    DoseNumber, VaccinationDate, MessageFormat, RawMessage, IsVerified
) VALUES
(
    @PersonID, 2, 'Charlotte Maxeke Hospital', 2, 'JNJ-2024-002-A',
    1, '2024-01-20', 'A',
    '8001015009087:Charlotte Maxeke Hospital:2024-01-20:JNJ-2024-002-A', 1
);

-- Person 3: 2 doses (AstraZeneca) — fully vaccinated
SELECT @PersonID = PersonID FROM dbo.Person WHERE IDNumber = '9203224800088';
INSERT INTO dbo.VaccinationRecord (
    PersonID, CenterID, CenterNameRaw, VaccineID, VaccineSerialNumber,
    DoseNumber, VaccinationDate, MessageFormat, RawMessage, IsVerified
) VALUES
(
    @PersonID, 3, 'Inkosi Albert Luthuli Hospital', 3, 'AZ-2024-003-A',
    1, '2024-01-10', 'A',
    '9203224800088:Inkosi Albert Luthuli Hospital:2024-01-10:AZ-2024-003-A', 1
),
(
    @PersonID, 3, 'Inkosi Albert Luthuli Hospital', 3, 'AZ-2024-003-B',
    2, '2024-02-07', 'B',
    'BAR-00003:2024-02-07:Inkosi Albert Luthuli Hospital:9203224800088', 1
);

PRINT 'Seed data inserted. Test IDs ready.';
GO
