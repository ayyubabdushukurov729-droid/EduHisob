# EduHisob

EduHisob is a multi-organization web application for independent learning centers. It is built with ASP.NET Core 10, Blazor Server, ASP.NET Core Identity, Entity Framework Core, and SQLite for local development.

## Run locally

Install the .NET 10 SDK, then from this directory run:

```powershell
dotnet tool restore
dotnet run
```

Open the local URL printed by `dotnet run`. The application applies EF Core migrations on startup. The local database is stored at `Data/app.db`; do not commit it.

## Implemented in this first working stage

- Public EduHisob landing page and center owner account registration/login.
- Center registration creates a separate organization, its owner account, a default branch, and a 14-day trial record in one database transaction.
- Persistent branch management and student create/search/list workflows.
- Center-scoped dashboard indicators for student statuses, new students, active groups, comparable month-to-date income, and monthly attendance; student lists can be filtered by status, branch, and search term.
- Read-only platform center directory and selected-center reports for enrollment, attendance, income, expenses, categories, and payment methods, protected by a separately configured platform administrator email.
- Course and group setup with weekly schedules, capacity, monthly fee, lessons, duration, teacher, room, and branch.
- Branch-specific room catalog with room capacity, optional group-to-room assignment, room/teacher schedule-conflict checks, and a weekly schedule view.
- Student enrollment is restricted to the same organization and branch, and group capacity is enforced.
- Group/date attendance for present, absent, late, and excused statuses; attendance is updated rather than duplicated for a student and lesson date.
- Center-scoped employee and teacher records with branch assignment and manually agreed monthly salaries.
- Monthly payroll summaries, manually recorded bonuses/advances/salary payouts, and linked finance expense records for actual payouts.
- Branch/date-filtered income and expense ledger with manual entry, optional student-linked tuition receipts, net period totals, and payroll payouts shown as expenses.
- Date/branch-filtered financial reports with daily/category/payment summaries, monthly comparison charts, Excel-compatible CSV download, and browser print-to-PDF.
- Monthly expense budgets by branch/category with warnings when recorded expenses reach or exceed the limit.
- Student debt estimates for completed months, based on active student/group memberships, current group monthly fees, and student-linked income receipts.
- Financial audit history for newly recorded income/expenses, payroll entries, and budget changes.
- Composite database foreign keys keep course, group, enrollment, and attendance data within its organization.
- ASP.NET Core Identity password hashing, account lockout, antiforgery protection, and server-side tenant ownership lookup.
- Responsive Uzbek interface.

## Not ready for public production

This repository is still under active development, not a finished SaaS launch. Debt reports use active group memberships and current group fees; historical fee changes and membership end dates are not stored. Financial audit history starts with new actions and does not backfill historical activity. Cash accounts, staff roles and permissions, and subscription enforcement have not yet been implemented. Registration currently collects a phone number but does not verify it. SMS/email delivery, password reset delivery, hosted PostgreSQL provisioning, external backup and restore testing, domain, hosting account, and production HTTPS have not been configured or tested. A Render deployment Blueprint is prepared, but no live service has been created. The Blueprint disables new registrations until account verification and recovery are configured. The local SQLite database is for development and is not a production deployment configuration.

The read-only platform center directory and reports are available at `/platforma/markazlar`. The report shows date- and branch-filtered aggregates and does not expose student-level records. The panel is restricted to the single account whose login email exactly matches the `PlatformAdmin:Email` configuration value (case-insensitive). Set the `PlatformAdmin__Email` environment variable to that account's email in the hosting environment; for local development, set `$env:PlatformAdmin__Email = "admin@example.com"` in PowerShell before starting the application. Leave this setting unset unless the account is authorized to view all center names, owner phone numbers, and aggregate reports. No platform administrator is enabled by default.

Do not expose this development instance to the public internet. Configure and test production infrastructure, phone verification, authorization, tenant-isolation, backup/restore, and HTTPS before onboarding real centers.

## Database migrations

Create a migration after changing the EF Core model:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName>
```

The project-local EF tooling is pinned in `dotnet-tools.json`.

## Render deployment preparation

The `render.yaml` Blueprint defines an ASP.NET Core web service, a private PostgreSQL database, persistent storage for Data Protection keys, and a health check. Production startup rejects SQLite, requires persistent Data Protection key storage at `/data/keys`, applies PostgreSQL migrations, and exposes `/healthz`. Local development continues to use SQLite and its existing migration history.

Before provisioning or deploying:

- Review Render's current prices before creating resources; the paid web service and persistent disk are required for a production deployment, and the PostgreSQL plan must include a tested backup/restore process.
- Create or sign in to a Render account and connect this repository. Review all Blueprint resources and billing before approving creation.
- The Blueprint keeps registration disabled until email/phone verification and account recovery are configured.
- Add `eduhisob.uz` in the Render service's custom-domain settings, then apply the exact DNS records Render provides at the domain's DNS provider. Do not guess the records.
- Verify HTTPS, authentication, tenant isolation, health checks, migrations, and a backup restore on the deployed service before onboarding centers.

Do not point the custom domain at the service or use it for real center data until these production checks are complete. Render account access, resource provisioning, backup policy, Data Protection key storage, domain DNS, and production smoke tests are not configured by this repository.
