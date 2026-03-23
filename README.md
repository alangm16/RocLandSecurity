RocLand Security
RocLand Security is a mobile application developed with .NET MAUI designed for the management and monitoring of security rounds. It allows guards to perform tours, scan control points via QR codes, and report incidents, ensuring operational continuity even in environments without internet connectivity.

Key Features
Cross-Platform Architecture: Compatible with Android, iOS, MacCatalyst, and Windows.

Offline-First Design: The system allows guards to work without an internet connection. Data is stored in a local SQLite database and synchronized automatically once a connection is detected.

QR Scanning: Integrated with ZXing.Net.Maui for real-time validation of control points.

Role-Based Access: Specialized interfaces and permissions for Guards and Supervisors.

Intelligent Synchronization: Automatic synchronization occurs during critical actions, upon reconnection, or via a background timer.

Technology Stack
Framework: .NET 10.0 (MAUI).

Local Database: SQLite using sqlite-net-pcl.

Server Database: SQL Server using Microsoft.Data.SqlClient.

Key Libraries:

ZXing.Net.Maui: Camera handling and QR/barcode scanning.

Microsoft.Maui.Controls: XAML-based user interface.

Data Architecture
The application implements a bidirectional synchronization strategy between local storage and a central server:

Local (SQLite): Acts as an offline mirror. It stores cached user credentials for offline login, shifts, rounds, control points, and incidents.

Server (SQL Server): Serves as the single source of truth. Supervisor data and control point catalogs are downloaded from the server, while guard activity is uploaded periodically.

Synchronization Triggers
Application launch with an active connection.

Completion of critical actions, such as finishing a round or reporting an incident.

Automatic detection of restored internet connectivity.

Background timer executed every 5 minutes.

Navigation
The project utilizes AppShell to manage tab-based navigation that changes dynamically based on the user's role:

Guard: Access to Rounds, History, and Profile.

Supervisor: Access to Control Panel, Incidents, General History, and Profile.

The Login page (MainPage) is handled modally to separate the authentication flow from the main navigation Shell.

Environment Setup
Requirements
Visual Studio 2022 or VS Code with the .NET MAUI workload.

.NET 10.0 SDK.

Accessible SQL Server instance.

Installation
Clone the repository.

Configure the connection string in MauiProgram.cs to point to your SQL Server database.

Restore NuGet packages.

Run the project on your preferred emulator or physical device.

Project Structure
/Models: Data entity definitions for SQL and SQLite.

/Services: Business logic, local database management, synchronization, and connectivity services.

/Views: User interfaces categorized by role (Guard, Supervisor) and shared components.

/Resources: Icons, custom fonts, and application imagery.
