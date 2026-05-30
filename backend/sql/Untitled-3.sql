-- Check if your background function logged the incoming message
SELECT *
FROM dbo.QueueMessageLog;

-- Check if the person or vaccination records were successfully updated/inserted
SELECT *
FROM dbo.Person;
SELECT *
FROM dbo.VaccinationRecord;

dbo.IsFullyVaccinated INT