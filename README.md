# FindIt Application

### Description
A mobile application designed to help ECU staff manage lost and found property around campus. Users can register and find items, update the item’s information, mark the items as claimed, and remove them from the database.
The application stores record locally, so they can be accessible at any time without an internet connection. With a connection, data becomes synchronized within a central server, so multiple staff members access the updated information.
Students will be able to find their lost items, search by name or campus location, as well as where to inquire to retrieve their belongings.

# Week 1 - Planning & Setup
This week, we've defined the purpose, scope, and initial architecture of FindIt. The application consists of a .NET MAUI mobile client, a local SQLite database, and an ASP.NET Core Web API backed by a central SQLite database.

The primary domain entity is `LostItem`. The current Week 1 model contains the following properties:

<img width="689" height="325" alt="image" src="https://github.com/user-attachments/assets/2df68643-eadf-4166-b229-54a0e116b351" />

Additional properties and image functionality will be added during later development.

## .NET MAUI Setup

A .NET MAUI application named `FindIt` was created and configured using .NET 11 Preview 7.

The project contains folders for:

- `Models`
- `Views`
- `ViewModels`
- `Services`
- `Data`

The Android project was successfully built with zero warnings and zero errors. The application was installed and launched successfully on a physical Android phone:

![FindIt running on Android](Screenshots/Week%201%20Mobile%20Device%20Test.png)

## Local SQLite Setup

The `sqlite-net-pcl` NuGet package was added to the MAUI project. The project uses version `1.11.285`, which replaced an older dependency that generated SQLite security and Android compatibility warnings.

A database service named `FindItDatabase` was created in the `Data` folder. The service:

- Stores the database in the application's local data directory
- Creates a database named `findit.db3`
- Uses `SQLiteAsyncConnection`
- Creates the local `LostItem` table during application startup
- Is registered as a singleton through .NET dependency injection

The local database initialization was successfully tested while running the application on an Android phone.

## Server Setup

An ASP.NET Core Web API project named `FindItServer` was created.

The server uses SQLite as its central database. When the server starts, it:

1. Creates a `Data` directory when needed
2. Creates or opens `findit-server.db3`
3. Creates the `LostItems` table when it does not already exist
4. Starts the ASP.NET Core server

The server currently provides the following endpoint:

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Confirms that the server is running |

The server project was successfully built with zero warnings and zero errors.

## Health Endpoint Test

The `/health` endpoint was tested at:

```text
http://localhost:5187/health
```
The server returned a successful response containing its status, service name, and current timestamp:

![Successful health endpoint response](Screenshots/Week%201%20Health%20Endpoint%20Test.png)

## Server Database Verification

The server database was opened using DB Browser for SQLite. The `LostItems` table was successfully created with the expected Week 1 columns:

![LostItems table in DB Browser for SQLite](Screenshots/Week%201%20LostItem%20Data%20Table.png)

## Project Structure

```text
mobile-dev-group-project
├── findit
│   ├── FindIt
│   │   ├── Data
│   │   │   └── FindItDatabase.cs
│   │   ├── Models
│   │   │   └── LostItem.cs
│   │   └── FindIt.csproj
│   ├── FindItServer
│   │   ├── Properties
│   │   │   └── launchSettings.json
│   │   ├── FindItServer.csproj
│   │   └── Program.cs
│   └── global.json
├── Screenshots
│   ├── Week 1 Health Endpoint.png
│   ├── Week 1 LostItem Data Table.png
│   └── Week 1 Mobile Device Test.png
├── .gitignore
└── README.md
```

## Running the Server

From the `findit/FindItServer` directory, run:

```powershell
dotnet restore
dotnet run --launch-profile http
```

Then open:

```text
http://localhost:5187/health
```

The server creates its SQLite database in:

```text
findit/FindItServer/Data/findit-server.db3
```

Generated database files are excluded from Git.

## Running the Android Application

To run the application on an Android device:

1. Enable Developer Options on the Android phone.
2. Temporarily disable Auto Blocker if using a Samsung device.
3. Enable USB debugging.
4. Connect the phone to the development computer.
5. Approve the computer's debugging authorization.
6. Build and deploy the `FindIt` MAUI project.
7. Disable USB debugging and re-enable Auto Blocker after testing.

The Android application requires Android API level 24 or later.

## Challenges

The team encountered several setup and compatibility challenges during Week 1:

- The project uses .NET 11 Preview 7, requiring every team member to use a compatible SDK and MAUI workload.
- Visual Studio attempted to use a newer .NET 11 release candidate instead of the version selected by the team.
- The Android build initially lacked Android API 37 and a valid JDK path.
- The original SQLite package version included outdated native dependencies that generated security and Android page-size warnings.
- The SQLite dependency was upgraded, after which the Android project built with zero warnings and zero errors.
- Samsung Auto Blocker temporarily prevented USB debugging during physical-device testing.

These issues were resolved without changing the project's selected .NET SDK version.

# Week 2 - CRUD and Change Tracking

During Week 2, the team implemented local create, update, and delete operations for lost-item records. These operations work offline against the SQLite database stored on the Android device.

## Create, Update, and Delete

The FindIt mobile interface now allows staff members to:

- Create a lost-item record with a name, description, location, date, status, and staff-member name
- View all active lost-item records stored on the device
- Edit an existing record and save its updated values
- Soft-delete a record so it is hidden from the active list without removing its history

Every operation refreshes the displayed information from SQLite after it completes. Required fields are validated before records are saved.

### Device UI Before Change

![Device UI before change](Screenshots/Week%202%20Device%20UI%20Before.png)

### Device UI After Change

![Device UI after change](Screenshots/Week%202%20Device%20UI%20After.png)

## Device Database Before and After Change

The `LostItem` table stores the current state of each item. Deleted records remain in the table with `IsDeleted` set to `true`, which preserves them for auditing and future synchronization.

### Device Table Before Change

![Device LostItem table before change](Screenshots/Week%202%20Device%20Table%20Before.png)

### Device Table After Change

![Device LostItem table after change](Screenshots/Week%202%20Device%20Table%20After.png)

## Change Tracking Mechanism

FindIt creates an immutable entry in the `ItemChanges` table whenever a lost item is created, updated, or deleted. Each entry records:

- The related lost-item identifier
- The operation type (`Created`, `Updated`, or `Deleted`)
- The UTC date and time of the operation
- The staff member who performed the operation
- A summary of the item state after the operation

The item update and its change-tracking entry are written within the same SQLite transaction. This prevents an item from changing without a corresponding audit record.

### Change Tracking Evidence

![ItemChanges audit records](Screenshots/Week%202%20Change%20Tracking.png)

## Week 2 Summary

Week 2 established the offline data-management workflow required by FindIt. Staff can now create, update, and soft-delete records on an Android device, and every operation is recorded in a separate audit table. Server synchronization remains planned for a later week.

# Week 3 - Server Synchronization

During Week 3, the team implemented one-way synchronization from the FindIt Android application to the FindIt server. Changes created while the device is offline remain in the local `ItemChanges` table until the server confirms that they were received and processed successfully.

## Push Endpoint

The server provides the following endpoint:

```text
POST /sync/push
```

The mobile application sends its pending create, update, and delete operations to this endpoint as a JSON batch. For every change, the request includes a unique change identifier, the operation type, timestamps, staff-member information, and the complete lost-item state.

The server processes the complete batch inside a SQLite transaction. It:

- Records each unique request in the `ReceivedChanges` table
- Uses the change identifier to prevent a retried request from being processed twice
- Inserts or updates the corresponding record in the server `LostItems` table
- Preserves soft-deleted records by storing their `IsDeleted` state
- Records when each received change was processed
- Returns the identifiers of all changes accepted by the server

## Sending and Clearing Pending Changes

The Android application displays the number of changes waiting in the local queue. A staff member enters the server address and selects **Send Pending Changes** to start synchronization.

The application removes local `ItemChanges` records only when their identifiers are included in a successful server response. If the request fails or the server cannot be reached, the pending records remain on the device so they can be retried later.

When testing on a physical Android phone, the server must listen on the computer's network interface and the phone must use the computer's LAN IP address, such as:

```text
http://192.168.1.25:5187
```

## Week 3 Evidence

The following screenshots document the completed device-to-server synchronization test:

### Server Database Before Changes Are Pushed

![Server database before changes are pushed](Screenshots/Week%203%20Server%20Database%20Before.png)

### Pending Changes on Device Waiting to Be Synchronized

![Pending changes on device before synchronization](Screenshots/Week%203%20Device%20Pending%20Before.png)

### Pending Changes on Device After Successful Synchronization

![Pending changes cleared after successful synchronization](Screenshots/Week%203%20Device%20Pending%20After.png)

### Server Database After Receiving Changes

![Server database after receiving device changes](Screenshots/Week%203%20Server%20Received%20Changes.png)

### Server Database After Processing Changes

![Server database after processing device changes](Screenshots/Week%203%20Server%20Processed%20Changes.png)

## Week 3 Summary

Week 3 connected the device's offline change queue to the server. Pending changes can now be sent in one batch, processed idempotently, and removed from the device only after server confirmation. This protects unsynchronized work from being lost when a network request fails.

# Week 4 - Pull Synchronization

During Week 4, the team implemented pull synchronization so an Android device can download new, updated, and deleted lost-item records from the server. The pull workflow is divided into download and processing stages so incoming data can be inspected before it changes the device's active records.

## Pull Endpoint

The server provides the following endpoint:

```text
POST /sync/pull
```

The device sends its unique device identifier and the UTC timestamp of its last completed pull. The server returns every `LostItems` row modified after that timestamp, including records whose `IsDeleted` value is true. The response also contains a batch identifier and a server watermark used as the next pull cursor.

For auditing, the server records each request in `DownloadBatches` and records the included item identifiers in `DownloadedChanges`.

## Downloading and Applying Incoming Changes

The Android application processes server changes in two explicit stages:

1. **Download Server Changes** stores the returned rows in the local `IncomingChanges` staging table without immediately changing `LostItem`.
2. **Apply Downloaded Changes** processes the staged rows in a SQLite transaction, advances the pull cursor, and acknowledges the completed batch to the server.

When a staged server row is processed:

- A record that does not exist on the device is inserted.
- A newer server version of an existing record updates the device record.
- An older server version is ignored so it cannot overwrite newer device data.
- A row with `IsDeleted` set to true becomes a local soft-delete tombstone and is removed from the active-items display.
- The staged row receives a `ProcessedAt` timestamp for audit evidence.

After the device applies the batch, it calls:

```text
POST /sync/pull/ack
```

The server then records the completion time in `DownloadBatches`. Incoming server changes do not create outbound `ItemChanges`, preventing downloaded records from being pushed back to the server as new local edits.

## Week 4 Evidence

The following screenshots document the completed pull-synchronization test on the Android device and server.

### Server Data Before Changes Are Pulled

The server contains a new record, an updated record, and soft-deleted records waiting to be downloaded by the device.

![Week 4 server data before pull](Screenshots/Week%204%20Server%20Data%20Before%20Pull.png)

### Device Tables Before Pull

Before the pull, the device contains the original versions of its local records and does not contain the new server-only record.

![Week 4 device tables before pull](Screenshots/Week%204%20Device%20Tables%20Before%20Pull.png)

### Device Tables After Download

After downloading, the server records are staged in `IncomingChanges`. The empty `ProcessedAt` values show that the records have not yet been applied to `LostItem`.

![Week 4 device tables after download part 1](Screenshots/Week%204%20Device%20Tables%20After%20Download1.png)

![Week 4 device tables after download part 2](Screenshots/Week%204%20Device%20Tables%20After%20Download2.png)

### Device Tables After Processing

After processing, the new server record is inserted, the existing record is updated, and server-side deletions are represented by local `IsDeleted` tombstones. The staging records also contain `ProcessedAt` timestamps.

![Week 4 device tables after processing part 1](Screenshots/Week%204%20Device%20Tables%20After%20Processing1.png)

![Week 4 device tables after processing part 2](Screenshots/Week%204%20Device%20Tables%20After%20Processing2.png)

### Server Database After Successful Download

The completed server download batch contains a populated `CompletedAt` value, proving that the device acknowledged successful processing.

![Week 4 server download complete](Screenshots/Week%204%20Server%20Download%20Complete1.png)

## Week 4 Summary

Week 4 completed the server-to-device half of synchronization. FindIt now downloads only records changed since the previous completed pull, stages them for inspection, applies inserts and updates transactionally, honors server-side soft deletes, and records a server acknowledgement after successful processing.

# Week 5 - Conflict Handling and Audit Trail

During Week 5, the team completed the two-way synchronization workflow by adding a deterministic conflict strategy, persistent audit logging, a single sync action, and automatic synchronization attempts.

## Conflict Strategy

FindIt uses a **last-write-wins** strategy based on each record's UTC `LastModified` value. When the server and device contain different versions of the same lost item, the version with the newest timestamp is retained.

- During a push, the server compares the incoming device timestamp with the current server timestamp. A newer or equal device version is applied; an older device version is accepted but does not overwrite the newer server record.
- During a pull, the device compares the downloaded timestamp with the local timestamp. A newer or equal server version is applied; an older server version is ignored.
- Every conflict decision records the item identifier, selected outcome, explanation, device identifier, and UTC time in the audit trail.

This strategy prevents an older edit from silently replacing a newer edit while ensuring that the same inputs always produce the same result.

## Audit Logging

The Android application stores synchronization evidence in its local `SyncAuditLogs` table. Each entry records:

- The operation, such as `ManualSync`, `AutomaticSync`, or `ConflictResolution`
- The outcome, such as `Succeeded`, `Failed`, `Offline`, `IncomingVersionApplied`, or `ServerVersionKept`
- A readable description of the event
- The related lost-item identifier when applicable
- The device identifier and UTC occurrence time
- Whether the record has been uploaded to the server

Pending audit records are sent to `POST /sync/audit`. The server stores them idempotently in its own `SyncAuditLogs` table, and the device marks only acknowledged records as uploaded.

## Manual and Automatic Sync

The **Sync Now** button performs the complete synchronization sequence: push pending device changes, resolve and record conflicts, apply any previously downloaded batch, pull and apply current server changes, acknowledge the download, and upload pending audit records.

FindIt also attempts the same workflow automatically when the main page appears and whenever network connectivity returns. If the device is offline, the app preserves all pending data and records an `AutomaticSync` audit entry with the `Offline` outcome. Online successes and network/server failures are also recorded, providing visible evidence of every attempt.

## Week 5 Evidence

The following screenshots document the completed Week 5 conflict, audit, and automatic-synchronization tests.

### Conflict Strategy Working

The device and server audit records show which version won after the same item was edited on both sides.

![Week 5 conflict strategy working](Screenshots/Week%205%20Conflict%20Strategy.png)

### Audit Logging on the Device

The app's **Sync Audit Trail** and the local `SyncAuditLogs` table show the recorded conflict and synchronization events.

![Week 5 device audit log](Screenshots/Week%205%20Device%20Audit%20Log.png)

### Audit Logging Synchronized to the Server

The server `SyncAuditLogs` table contains the device audit identifiers and their operation, outcome, details, device ID, and timestamps.

![Week 5 server audit log](Screenshots/Week%205%20Server%20Audit%20Log.png)

### Automatic Sync Attempt - Online

An `AutomaticSync` record with a successful online outcome shows that synchronization ran without selecting **Sync Now**.

![Week 5 automatic sync online](Screenshots/Week%205%20Automatic%20Sync%20Online.png)

### Automatic Sync Attempt - Offline

An `AutomaticSync` record with the `Offline` outcome shows that the attempt was recorded and pending data remained safe when no network was available.

![Week 5 automatic sync offline](Screenshots/Week%205%20Automatic%20Sync%20Offline.png)

## Week 5 Summary

Week 5 added predictable last-write-wins conflict resolution, persistent audit evidence on both the Android device and server, a one-button full sync workflow, and automatic online/offline sync attempts. Together these features make synchronization behavior traceable and prevent older records from overwriting newer work.
