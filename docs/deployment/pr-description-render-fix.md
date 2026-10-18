# Pull Request: Normalize Render PostgreSQL URI to ADO.NET & Enable Startup Auto-Migration

## 🛠️ Fix: Normalize Render PostgreSQL Connection URI to ADO.NET & Enable Startup Auto-Migration

### 📌 Summary
Resolves container startup crashes on Render caused by `NpgsqlConnectionStringBuilder` rejecting PostgreSQL URI strings (`postgresql://...`) when appended with key-value parameters. Also configures runtime auto-migration (`AUTO_MIGRATE=true`) so that EF Core automatically applies all 21 database tables and baseline seeds upon initial cloud container startup.

---

### 🔍 Problem & Root Cause Analysis
During initial deployment on Render:
1. **Connection String Crash:** Render provisions Managed PostgreSQL and passes the connection string via environment variables as a standard URI (`postgresql://user:password@host/database`).
2. `SystemSecretsConfiguration.EnsureTlsConnectionString` was previously appending `;SslMode=Require` directly to the raw connection string.
3. Because the string was a URI rather than semicolon-delimited key-value pairs, `NpgsqlConnectionStringBuilder` failed to parse the parameter, throwing:
   ```text
   System.ArgumentException: Couldn't set postgresql://...;sslmode
     ---> System.Collections.Generic.KeyNotFoundException: The given key was not present in the dictionary.
     at CivicConnect.Data.Configuration.SystemSecretsConfiguration.EnforceLeastPrivilegeRoleAsync(...)
   ```
4. **Schema Initialization:** In production container environments where external CLI tools (`dotnet ef`) are not executed in the entrypoint, the database tables need to be created on first boot without manual intervention.

---

### 🚀 Key Changes
1. **URI Normalization (`NormalizeToAdoNetConnectionString`)**:
   - Implemented `SystemSecretsConfiguration.NormalizeToAdoNetConnectionString(string rawConnectionString)`.
   - Uses `System.Uri` to safely extract `Host`, `Port`, `Database`, `Username`, and `Password`.
   - Emits a clean, standard ADO.NET connection string:
     `Host=<host>;Port=<port>;Database=<db>;Username=<user>;Password=<pass>;SslMode=Require`.
   - Leaves standard ADO.NET formatted connection strings untouched while ensuring TLS/SSL enforcement.

2. **Automated Migration on Startup (`AUTO_MIGRATE=true`)**:
   - Updated `Program.cs` in both `CivicConnect.Api` and `CivicConnect.Web` to check `AUTO_MIGRATE` (or `DOTNET_RUNNING_IN_CONTAINER`).
   - Automatically executes `dbContext.Database.MigrateAsync()` during host initialization, provisioning the full 21-table schema, views (`staff_directory`), and baseline reference seeds.

3. **Compiler Cleanliness**:
   - Removed obsolete `TrustServerCertificate` call, maintaining zero compilation warnings.

---

### 🧪 Verification & Testing
- **Local Unit & Data Tests:** 100% pass across all test suites:
  - `CivicConnect.Data.Tests`: 65 passed, 0 failed.
  - `CivicConnect.Api.Tests`: 177 passed, 0 failed.
  - `CivicConnect.Web.Tests`: 44 passed, 0 failed.
- **Connection Normalization Test:** Verified with standard ADO.NET strings, local PostgreSQL URIs, and Render `postgresql://` connection strings with URL-encoded passwords.
- **Zero Warnings:** `0 Warning(s)`, `0 Error(s)`.

---

### 📋 Checklist
- [x] Code builds cleanly without warnings or errors
- [x] All automated tests pass (286/286)
- [x] Tested against Render's PostgreSQL URI format
- [x] Auto-migration tested for container deployments
- [x] Compliant with `GEMINI.md` database conventions and NFR-04 security guidelines
