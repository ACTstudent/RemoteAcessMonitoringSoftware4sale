# Tracked source and artifact inventory

Baseline: b6920e4ebe581a961cf9a8236f45b6e8155584e9. Generated from git ls-files on 2026-09-05 before adding this test documentation.

All rows start UNREVIEWED. Area assignments are based on paths and require behavioral mapping using TEST-CASES.md; they do not claim test coverage. Track executed evidence in RUN-REPORT-TEMPLATE.md. New testing documents are governed by DOC-01.

| File | Verification area / case families | Mapping status |
| --- | --- | --- |
| `.github/workflows/ci-full.yml` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `.github/workflows/nuget-vulnerability-gate.yml` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `.github/workflows/pages.yml` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `.github/workflows/release.yml` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `.github/workflows/test.yml` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `.gitignore` | BLD-01; DEP-01; DOC-01 | UNREVIEWED |
| `CAMS-Guide.md` | DOC-01 | UNREVIEWED |
| `DEPLOYMENT.md` | DOC-01 | UNREVIEWED |
| `DIAGRAMS/ERD.md` | DOC-01 | UNREVIEWED |
| `DIAGRAMS/Flowchart.md` | DOC-01 | UNREVIEWED |
| `DIAGRAMS/Menu-Structure-Diagram.md` | DOC-01 | UNREVIEWED |
| `DIAGRAMS/SignalR-Message-Flow.md` | DOC-01 | UNREVIEWED |
| `DIAGRAMS/Use-Case-Diagram.md` | DOC-01 | UNREVIEWED |
| `LICENSE` | DOC-01 | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/BrowserUrlCollectorTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/Client.Tests.csproj` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/ClientSettingsStoreTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/DurableTelemetryQueueTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/InputSimulatorTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/ManagedBrowserCollectorTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client.Tests/ServerDiscoveryClientTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Client/Client.csproj` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/InputSimulator.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/MainForm.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Program.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/BrowserUrlCollector.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/ClientResilienceOptions.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/ClientSettingsStore.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/DurableTelemetryQueue.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/IMonitoringHubClient.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/IScreenCaptureService.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/ManagedBrowserCollector.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/MonitoringHubClient.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/ScreenCaptureService.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/Services/ServerDiscoveryClient.cs` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/Client/client-settings.json` | WIN-01..02; HUB/TEL/POL/NET; AUTH/SES | UNREVIEWED |
| `Monitoring And Remote Access/RemoteMonitoring.sln` | BLD-01; DEP-01; DOC-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/AccountControllerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/AdminControllerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/AdminDatabaseControllerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/AdminDeploymentControllerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/AdminHistoricalArchiveSqliteTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/DeploymentPingControllerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Controllers/TeacherControllerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Data/DatabaseInitializerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Hubs/RemoteMonitoringHubSecurityTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Server.Tests.csproj` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/AccountSeederTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/AnalyticsServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/AuthPrincipalFactoryTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/AuthenticationServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/CategoryPolicyEngineTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/ClassManagementServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/DatabaseMaintenanceServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/DeploymentServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/LabSessionLifecycleServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/MonitoringServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/ServerCertificateManagerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/SessionManagerServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/TelemetryRetentionCleanerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/TelemetryServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/WebsiteDomainNormalizerTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Services/WorkstationRegistrationServiceTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Shared/ContractTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/Shared/PolicyPatternMatcherTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server.Tests/SystemAndModelTests.cs` | BLD-01; review assertions and map domain cases | UNREVIEWED |
| `Monitoring And Remote Access/Server/Authorization/ActiveTeacherAuthorizationFilter.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Authorization/AdminControllerAuthorizationFilter.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Authorization/TeacherSharedActionAttribute.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/AccountController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/AdminController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/AdminDatabaseController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/AdminDeploymentController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/ClientAuthController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/DeploymentPingController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/MonitoringController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/StudentController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Controllers/TeacherController.cs` | AUTH-01..05; CRUD/SES/ALT/REP/DEP/UI | UNREVIEWED |
| `Monitoring And Remote Access/Server/Data/ApplicationDbContext.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Data/ApplicationDbContextFactory.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Extensions/DateTimeDisplayExtensions.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Hubs/RemoteMonitoringHub.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260829150656_InitialCreate.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260829150656_InitialCreate.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260829175729_AddComputerStatusHistory.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260829175729_AddComputerStatusHistory.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830060854_CompleteRemainingScope.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830060854_CompleteRemainingScope.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830081623_CompleteAlertLifecycleAndAnalyticsIndexes.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830081623_CompleteAlertLifecycleAndAnalyticsIndexes.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830113116_AddBrowserMonitoringHistory.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830113116_AddBrowserMonitoringHistory.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830183805_ScopeTeacherRestrictions.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260830183805_ScopeTeacherRestrictions.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260831091958_AddPauseAndStationCollation.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260831091958_AddPauseAndStationCollation.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260831092112_EnforceSessionAndWorkstationIntegrity.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260831092112_EnforceSessionAndWorkstationIntegrity.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260901123021_FixClassCrudAndWorkstationRegistration.Designer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/20260901123021_FixClassCrudAndWorkstationRegistration.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Migrations/ApplicationDbContextModelSnapshot.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/ActivityEvent.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Admin.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/AnalyticsModels.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/ApplicationCategory.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/AuditLog.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/BlacklistItem.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/BrowserMonitoringRecord.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Class.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/ClassStudent.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Computer.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/ComputerStatusHistory.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/DeploymentViewModel.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/IdleInterval.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/LabSession.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/LanConfiguration.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/MonitoringAlert.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Notification.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/PasswordChangeInput.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Permission.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/PolicyManagementViewModel.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/RemoteCommandLog.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/RemoteControlSession.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/RestrictionRule.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Role.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/SessionRule.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Student.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/SystemLog.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/Teacher.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/UsageLog.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/WebsiteCategory.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Models/WebsiteUsageLog.cs` | DB-01; SES/CRUD/TEL/REP domain invariants | UNREVIEWED |
| `Monitoring And Remote Access/Server/Program.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Server.csproj` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/AccountSeeder.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/AnalyticsService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/AuthPrincipalFactory.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/AuthenticationService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/CategoryPolicyEngine.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/ClassManagementService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/DatabaseInitializer.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/DatabaseMaintenanceService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/DatabaseRestoreStartup.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/DeploymentService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/ExpiredLabSessionCleanupService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/IAuthenticationService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/IDatabaseMaintenanceService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/IDeploymentService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/IMonitoringService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/ITelemetryService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/LabSessionLifecycleService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/MonitoringService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/PolicyChangeBroadcastInterceptor.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/ServerCertificateManager.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/ServerDiscoveryService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/SessionHelper.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/SessionManagerService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/TelemetryRetentionCleanupService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/TelemetryRetentionOptions.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/TelemetryService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Services/WorkstationRegistrationService.cs` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Account/Login.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/AuditLogs.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Blacklists.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/ClassDetails.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Classes.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/ComputerHistory.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Computers.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Index.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/LanConfig.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Reports.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Restrictions.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Roles.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/SessionRules.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Settings.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Students.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/SystemLogs.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Teachers.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/Whitelists.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Admin/_Layout.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/AdminDatabase/Index.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/AdminDeployment/Index.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Shared/_ConfirmModal.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Shared/_Toasts.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Student/Alerts.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Student/Index.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Student/Settings.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Student/_StudentLayout.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/AlertHistory.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Alerts.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/BrowserMonitoringHistory.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/ClassAnalytics.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/ClassDetails.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Classes.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Computers.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Dashboard.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/LabUtilization.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Monitoring.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Records.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/RemoteHistory.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Restrictions.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Sessions.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Settings.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/StudentDetails.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/Students.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/UnifiedTimeline.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/_Layout.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/_PolicyCategoryTable.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/Teacher/_TeacherLayout.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/Views/_ViewImports.cshtml` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/appsettings.json` | AUTH/SES/TEL/DB/TLS/NET/DEP/REP; BLD-01 | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/css/site.css` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/images/pardo_logo.png` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/js/site.js` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/lib/bootstrap-icons/bootstrap-icons.min.css` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/lib/bootstrap-icons/fonts/bootstrap-icons.woff` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/lib/bootstrap-icons/fonts/bootstrap-icons.woff2` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/lib/bootstrap/css/bootstrap.min.css` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/lib/bootstrap/js/bootstrap.bundle.min.js` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Server/wwwroot/lib/signalr/signalr.min.js` | UI-01..02; SEC-01; business flow cases (vendor assets: integration only) | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/BrowserMonitoring.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/ClientAuthMessages.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/ControlMessages.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/GlobalSessionMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/HubEventNames.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/HubMethodNames.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/InfractionMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/PolicyPatternMatcher.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/RemoteCommandResult.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/RemoteControlStateMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/RemoteInputMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/RestrictionRuleMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/ScreenFrameMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/StudentConnectionMessage.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/TelemetryMessages.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Contracts/WebsiteDomainNormalizer.cs` | HUB-01..03; POL-01; TEL-01 | UNREVIEWED |
| `Monitoring And Remote Access/Shared/Shared.csproj` | BLD-01; DEP-01; DOC-01 | UNREVIEWED |
| `README.md` | DOC-01 | UNREVIEWED |
| `build-everything.ps1` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `client-dist/CAMS-Client-Setup.exe` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `client-dist/CAMS-Client-Setup.exe.sha256` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `client-installer.iss` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `portal/README.md` | WEB-01; UI-02; DOC-01 | UNREVIEWED |
| `portal/assets/app.js` | WEB-01; UI-02; DOC-01 | UNREVIEWED |
| `portal/assets/cams-mark.svg` | WEB-01; UI-02; DOC-01 | UNREVIEWED |
| `portal/assets/styles.css` | WEB-01; UI-02; DOC-01 | UNREVIEWED |
| `portal/index.html` | WEB-01; UI-02; DOC-01 | UNREVIEWED |
| `portal/version.json` | WEB-01; UI-02; DOC-01 | UNREVIEWED |
| `server-dist/CAMS-Server-Setup.exe` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `server-dist/CAMS-Server-Setup.exe.sha256` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `server-dist/release-manifest.json` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `server-installer.iss` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `start-server.bat` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `test-installer.ps1` | BLD-01; INS-01..02; DEP-01; DOC-01 | UNREVIEWED |
| `tools/CamsDbCleaner/CamsDbCleaner.csproj` | TOOL-01; BLD-01 | UNREVIEWED |
| `tools/CamsDbCleaner/Program.cs` | TOOL-01; BLD-01 | UNREVIEWED |
| `tools/CamsDbCleaner/README.md` | TOOL-01; BLD-01 | UNREVIEWED |
| `version.json` | BLD-01; DEP-01; DOC-01 | UNREVIEWED |
