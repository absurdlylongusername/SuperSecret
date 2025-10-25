# SuperSecret - Secret Link Generator

A small ASP.NET Core application for generating unique, time-limited secret links.

## Features

- Generate unique links that display personalized secret messages
- Support for single-use and multi-use links
- Optional expiry dates for links
- Admin UI and REST API for link generation
- SQL Server storage with Dapper
- HMAC-signed JWT tokens for security

## Prerequisites

- .NET 9 SDK
- Docker Desktop (for SQL Server)
- SQL Server (or use Docker Compose)

## Getting Started

### 1. Start SQL Server (using Docker)

```bash
docker-compose up -d
```

Wait for SQL Server to be healthy (about 30 seconds).

### 2. Create Database Schema

Connect to SQL Server and run the `DatabaseSchema.sql` script:

```bash
# Using sqlcmd (Windows)
sqlcmd -S localhost,1433 -U sa -P "YourStrong@Passw0rd" -i SuperSecret/DatabaseSchema.sql

# Using Azure Data Studio or SQL Server Management Studio
# Open DatabaseSchema.sql and execute it
```

### 3. Configure Application Secrets (Recommended for Development)

Instead of using `appsettings.json`, use user secrets:

```bash
cd SuperSecret
dotnet user-secrets set "TokenSigningKey" "your-very-secret-key-that-is-at-least-32-characters-long"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=SuperSecretDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
```

### 4. Run the Application

```bash
cd SuperSecret
dotnet run
```

The application will be available at:
- HTTPS: https://localhost:7xxx
- HTTP: http://localhost:5xxx

## Usage

### Admin UI

Navigate to `/admin` to create links via the web interface:
- Enter a username (1-50 alphanumeric characters)
- Specify max clicks (default: 1 for single-use)
- Optionally set an expiry date
- Click "Generate Link" to create the secret link

### API

Create links programmatically:

```bash
POST /api/links
Content-Type: application/json

{
  "username": "John",
"max": 3,
  "expiresAt": "2024-12-31T23:59:59Z"
}
```

Response:
```json
{
  "url": "https://localhost:7xxx/supersecret/{token}"
}
```

### Accessing Secret Links

Visit the generated link: `/supersecret/{token}`

- **First valid visit**: "You have found the secret, {username}!"
- **Subsequent/invalid visits**: "There are no secrets here"

## Architecture

### Token Format

Tokens use HMAC-SHA256 signed JWT format:
- `sub`: Username
- `jti`: Unique ID (ULID-style)
- `max`: Maximum clicks allowed
- `exp`: Expiry timestamp (optional)
- `ver`: Token version

### Database Schema

**SingleUseLinks**: Presence-only table for single-use links
- Deletes row on first valid access
- Fast lookups with primary key

**MultiUseLinks**: Countdown table for multi-use links
- Decrements `ClicksLeft` on each access
- Deletes when clicks reach zero

### Security

- Constant-time HMAC comparison prevents timing attacks
- Tokens are signed and validated on every request
- No-store cache headers prevent browser caching
- SQL injection protected by parameterized queries (Dapper)

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=SuperSecretDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
  },
  "TokenSigningKey": "your-secret-key-min-32-chars-long"
}
```

### Environment Variables

- `ConnectionStrings__DefaultConnection`: Database connection string
- `TokenSigningKey`: HMAC signing key (min 32 characters)

## Database Maintenance

### Purge Expired Links

Run periodically (e.g., daily via scheduled job):

```sql
DELETE FROM dbo.SingleUseLinks WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
DELETE FROM dbo.MultiUseLinks WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
```

## Project Structure

```
SuperSecret/
??? Models/
?   ??? ApiModels.cs     # API request/response models
?   ??? SecretLinkClaims.cs   # Token claims model
??? Services/
?   ??? ITokenService.cs      # Token service interface
?   ??? TokenService.cs     # HMAC JWT token implementation
?   ??? ILinkStore.cs      # Data store interface
?   ??? SqlLinkStore.cs       # Dapper SQL implementation
??? Pages/
?   ??? Admin/
?   ?   ??? Index.cshtml# Admin UI
?   ?   ??? Index.cshtml.cs
?   ??? SuperSecret.cshtml    # Secret reveal page
?   ??? SuperSecret.cshtml.cs
??? DatabaseSchema.sql        # SQL Server schema
??? docker-compose.yml # SQL Server Docker setup
??? Program.cs     # Application entry point
```

## License

MIT
