-- ---------------------------------------------------------------------------
-- Demo data for LOCAL development. docker-compose.dev.yml runs this once, when
-- the MariaDB volume is empty. It is the same home the mockup/Angular app
-- shows: 5 rooms, 10 devices, 30 days of hourly measurements, and the command
-- events the lamps' on/off state is derived from.
--
-- Do NOT run this against a real database - it inserts blindly.
-- All timestamps are naive UTC, like the API itself writes (DateTime.UtcNow).
--
-- Conventions the frontend relies on:
--   Device.Type    = 'lamp' | 'thermometer' | 'motion' | 'humidity'
--   SensorType     = 'temperature' | 'humidity' | 'motion'  (Unit: '°C' | '%' | 'bool')
--   EventLog.Event = 'CommandIssued' with Description "Command 'ON' issued to device 'X'."
--                    (the format the API writes; the app parses the command out of it)
-- ---------------------------------------------------------------------------

USE smarthome;

SET NAMES utf8mb4;

-- ---- Rooms -----------------------------------------------------------------
INSERT INTO Room (RoomID, Name) VALUES
    (1, 'Stue'),
    (2, 'Køkken'),
    (3, 'Soveværelse'),
    (4, 'Badeværelse'),
    (5, 'Garage');

-- ---- Devices ---------------------------------------------------------------
INSERT INTO Device (DeviceID, Name, Type, RoomID, MACAddress, IPv4Address, Status, RegistrationDate, LastSeen) VALUES
    ( 1, 'Loftlampe',        'lamp',        1, 'AA:BB:CC:00:00:01', '192.168.1.51', 'Online',  UTC_TIMESTAMP() - INTERVAL 81 DAY, UTC_TIMESTAMP() - INTERVAL 2 MINUTE),
    ( 2, 'Gulvlampe',        'lamp',        1, 'AA:BB:CC:00:00:02', '192.168.1.52', 'Online',  UTC_TIMESTAMP() - INTERVAL 82 DAY, UTC_TIMESTAMP() - INTERVAL 3 MINUTE),
    ( 3, 'Temperatur',       'thermometer', 1, 'AA:BB:CC:00:00:03', '192.168.1.57', 'Online',  UTC_TIMESTAMP() - INTERVAL 87 DAY, UTC_TIMESTAMP() - INTERVAL 2 MINUTE),
    ( 4, 'Bevægelse',        'motion',      1, 'AA:BB:CC:00:00:04', '192.168.1.53', 'Online',  UTC_TIMESTAMP() - INTERVAL 83 DAY, UTC_TIMESTAMP() - INTERVAL 4 MINUTE),
    ( 5, 'Bordlampe',        'lamp',        2, 'AA:BB:CC:00:00:05', '192.168.1.54', 'Online',  UTC_TIMESTAMP() - INTERVAL 84 DAY, UTC_TIMESTAMP() - INTERVAL 5 MINUTE),
    ( 6, 'Temperatur',       'thermometer', 2, 'AA:BB:CC:00:00:06', '192.168.1.55', 'Online',  UTC_TIMESTAMP() - INTERVAL 85 DAY, UTC_TIMESTAMP() - INTERVAL 4 MINUTE),
    ( 7, 'Natlampe',         'lamp',        3, 'AA:BB:CC:00:00:07', '192.168.1.56', 'Online',  UTC_TIMESTAMP() - INTERVAL 86 DAY, UTC_TIMESTAMP() - INTERVAL 6 MINUTE),
    ( 8, 'Temperatur',       'thermometer', 3, 'AA:BB:CC:00:00:08', '192.168.1.58', 'Online',  UTC_TIMESTAMP() - INTERVAL 88 DAY, UTC_TIMESTAMP() - INTERVAL 6 MINUTE),
    ( 9, 'Luftfugtighed',    'humidity',    4, 'AA:BB:CC:00:00:09', '192.168.1.59', 'Online',  UTC_TIMESTAMP() - INTERVAL 89 DAY, UTC_TIMESTAMP() - INTERVAL 3 MINUTE),
    (10, 'Bevægelsessensor', 'motion',      5, 'AA:BB:CC:00:00:10', '192.168.1.60', 'Offline', UTC_TIMESTAMP() - INTERVAL 90 DAY, UTC_TIMESTAMP() - INTERVAL 27 HOUR);

-- ---- 30 days of hourly measurements ---------------------------------------
-- A daily rhythm (sine) plus a small deterministic skew, so the curves are not
-- perfectly smooth arcs.
INSERT INTO SensorData (DeviceID, SensorType, Value, Unit, Timestamp)
WITH RECURSIVE hours(n) AS (
    SELECT 1
    UNION ALL
    SELECT n + 1 FROM hours WHERE n < 720
)
SELECT s.dev,
       s.stype,
       ROUND(s.anchor
             + s.amp * SIN(((n % 24) / 24) * 2 * PI())
             + (((n * 37 + s.dev) % 7) - 3) * s.amp * 0.08, 2),
       s.unit,
       UTC_TIMESTAMP() - INTERVAL n HOUR
FROM hours
CROSS JOIN (
             SELECT 3 AS dev, 'temperature' AS stype, 21 AS anchor, 1.6 AS amp, '°C' AS unit
   UNION ALL SELECT 3,         'humidity',            43,            6.0,        '%'
   UNION ALL SELECT 6,         'temperature',         23,            1.6,        '°C'
   UNION ALL SELECT 8,         'temperature',         20,            1.6,        '°C'
   UNION ALL SELECT 9,         'humidity',            68,            6.0,        '%'
) AS s;

-- The newest sample per sensor is exactly the mockup value shown on the tiles.
INSERT INTO SensorData (DeviceID, SensorType, Value, Unit, Timestamp) VALUES
    (3, 'temperature', 21, '°C', UTC_TIMESTAMP() - INTERVAL 2 MINUTE),
    (3, 'humidity',    43, '%',  UTC_TIMESTAMP() - INTERVAL 2 MINUTE),
    (6, 'temperature', 23, '°C', UTC_TIMESTAMP() - INTERVAL 4 MINUTE),
    (8, 'temperature', 20, '°C', UTC_TIMESTAMP() - INTERVAL 6 MINUTE),
    (9, 'humidity',    68, '%',  UTC_TIMESTAMP() - INTERVAL 3 MINUTE);

-- ---- Motion: sparse events, not a curve -----------------------------------
INSERT INTO SensorData (DeviceID, SensorType, Value, Unit, Timestamp)
WITH RECURSIVE days(n) AS (
    SELECT 1
    UNION ALL
    SELECT n + 1 FROM days WHERE n < 29
)
SELECT 4, 'motion', 1, 'bool', UTC_TIMESTAMP() - INTERVAL n DAY - INTERVAL ((n * 7) % 10) HOUR FROM days
UNION ALL
SELECT 10, 'motion', 1, 'bool', UTC_TIMESTAMP() - INTERVAL n DAY - INTERVAL ((n * 5) % 10) HOUR
FROM days WHERE n % 2 = 0;

-- Latest motion in the living room (the time the tile shows) and the garage
-- sensor that went quiet.
INSERT INTO SensorData (DeviceID, SensorType, Value, Unit, Timestamp) VALUES
    ( 4, 'motion', 1, 'bool', UTC_TIMESTAMP() - INTERVAL 38 MINUTE),
    (10, 'motion', 1, 'bool', UTC_TIMESTAMP() - INTERVAL 27 HOUR);

-- ---- Event log ------------------------------------------------------------
-- Registrations plus the commands the lamps' state is derived from (the API has
-- no state field - see smart-home/docs/API-NOTES.md).
INSERT INTO EventLog (DeviceID, Event, Description, Timestamp)
SELECT DeviceID,
       'DeviceRegistered',
       CONCAT('Device ''', Name, ''' (', MACAddress, ') was registered.'),
       RegistrationDate
FROM Device;

INSERT INTO EventLog (DeviceID, Event, Description, Timestamp) VALUES
    ( 1, 'CommandIssued', 'Command ''ON'' issued to device ''Loftlampe''.',   UTC_TIMESTAMP() - INTERVAL 12 MINUTE),
    ( 2, 'CommandIssued', 'Command ''OFF'' issued to device ''Gulvlampe''.',  UTC_TIMESTAMP() - INTERVAL 3 HOUR),
    ( 5, 'CommandIssued', 'Command ''OFF'' issued to device ''Bordlampe''.',  UTC_TIMESTAMP() - INTERVAL 5 HOUR),
    ( 7, 'CommandIssued', 'Command ''OFF'' issued to device ''Natlampe''.',   UTC_TIMESTAMP() - INTERVAL 9 HOUR),
    (10, 'DeviceOffline', 'Device stopped responding.',                       UTC_TIMESTAMP() - INTERVAL 26 HOUR);
