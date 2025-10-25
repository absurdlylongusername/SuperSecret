# SuperSecret - Quick Start Guide

## ?? 5-Minute Setup

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

### Setup Steps

#### 1. Start Database
```bash
docker-compose up -d
```

#### 2. Create Database Schema
Wait 30 seconds for SQL Server to start, then run:

**Windows:**
```bash
sqlcmd -S localhost,1433 -U sa -P "YourStrong@Passw0rd" -i SuperSecret\DatabaseSchema.sql -C
```

**Linux/Mac:**
```bash
sqlcmd -S localhost,1433 -U sa -P "YourStrong@Passw0rd" -i SuperSecret/DatabaseSchema.sql -C
```

Or use the setup script:
- **Windows:** `setup.bat`
- **Linux/Mac:** `./setup.sh`

#### 3. Run the App
```bash
cd SuperSecret
dotnet run
```

Visit: **https://localhost:7xxx/admin**

---

## ?? Quick Examples

### Create a Link (UI)
1. Go to `/admin`
2. Enter username: `Alice`
3. Click "Generate Link"
4. Copy and share the link

### Create a Link (API)
```bash
curl -X POST https://localhost:7001/api/links \
  -H "Content-Type: application/json" \
  -d '{"username":"Bob","max":1}'
```

### Test a Link
Visit the generated URL - first time shows the secret, subsequent visits show neutral message.

---

## ?? Common Tasks

### Change Database Password
1. Update `docker-compose.yml` - change `SA_PASSWORD`
2. Update `appsettings.Development.json` - change connection string password
3. Restart: `docker-compose down && docker-compose up -d`

### Use User Secrets (Recommended)
```bash
cd SuperSecret
dotnet user-secrets set "TokenSigningKey" "your-secret-key-here"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=..."
```

### View Database
Use Azure Data Studio or SSMS:
- **Server:** localhost,1433
- **User:** sa
- **Password:** YourStrong@Passw0rd

### Clean Up Expired Links
```sql
DELETE FROM dbo.SingleUseLinks WHERE ExpiresAt <= SYSUTCDATETIME();
DELETE FROM dbo.MultiUseLinks WHERE ExpiresAt <= SYSUTCDATETIME();
```

---

## ?? Troubleshooting

### "Cannot connect to SQL Server"
- Ensure Docker is running: `docker ps`
- Check SQL Server logs: `docker logs supersecret-sql`
- Wait 30-60 seconds after starting

### "TokenSigningKey not configured"
- Add to `appsettings.Development.json` or use user secrets
- Must be at least 32 characters long

### "Username must be alphanumeric"
- Only letters and numbers (A-Z, a-z, 0-9)
- No spaces or special characters
- Max 50 characters

### Port Already in Use
Change ports in `docker-compose.yml`:
```yaml
ports:
  - "1434:1433"  # Use 1434 instead of 1433
```

---

## ?? More Information

- **Full Documentation:** [README.md](README.md)
- **API Reference:** [API_DOCUMENTATION.md](API_DOCUMENTATION.md)
- **Database Schema:** [SuperSecret/DatabaseSchema.sql](SuperSecret/DatabaseSchema.sql)

---

## ?? Security Notes

- **Development Only:** Default passwords are for development only
- **Production:** Use Azure Key Vault or environment variables for secrets
- **HTTPS:** Always use HTTPS in production
- **Rate Limiting:** Consider adding rate limiting for production use

---

## ?? Customization

### Change Token Expiry Format
Edit `TokenService.cs` - modify the `exp` claim handling

### Change Database
Replace `SqlLinkStore.cs` with your own `ILinkStore` implementation (e.g., SQLite, PostgreSQL, Redis)

### Customize UI
Edit `Pages/Admin/Index.cshtml` and `Pages/SuperSecret.cshtml`

### Add Authentication
Add authentication middleware in `Program.cs` before the `/admin` routes

---

## ?? Monitoring

### View Active Links
```sql
-- Count active links
SELECT 
    (SELECT COUNT(*) FROM dbo.SingleUseLinks) AS SingleUseCount,
    (SELECT COUNT(*) FROM dbo.MultiUseLinks) AS MultiUseCount,
    (SELECT SUM(ClicksLeft) FROM dbo.MultiUseLinks) AS TotalClicksRemaining;
```

### View Recent Links
```sql
SELECT TOP 10 Jti, CreatedAt, ExpiresAt 
FROM dbo.SingleUseLinks 
ORDER BY CreatedAt DESC;
```

---

## ? Production Checklist

- [ ] Change default SQL Server password
- [ ] Use environment variables or Key Vault for secrets
- [ ] Enable HTTPS only
- [ ] Add authentication to `/admin` route
- [ ] Add rate limiting
- [ ] Set up automated expired link cleanup (scheduled job)
- [ ] Configure proper logging and monitoring
- [ ] Review and harden database permissions
- [ ] Add CORS policy if needed for API
- [ ] Test error handling and edge cases

---

**Need Help?** Check the full [README.md](README.md) or [API_DOCUMENTATION.md](API_DOCUMENTATION.md)
