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
