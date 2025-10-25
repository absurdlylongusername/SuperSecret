# SuperSecret API Documentation

## Base URL

```
https://localhost:7xxx
```

## Endpoints

### Create Link

Creates a new secret link with optional expiry and click limits.

**Endpoint:** `POST /api/links`

**Request Headers:**
```
Content-Type: application/json
```

**Request Body:**
```json
{
  "username": "string",      // Required: 1-50 alphanumeric characters only
  "max": 1,                  // Optional: Max clicks allowed (default: 1)
  "expiresAt": "2024-12-31T23:59:59Z"  // Optional: ISO 8601 datetime
}
```

**Response:** `200 OK`
```json
{
  "url": "https://localhost:7xxx/supersecret/{token}"
}
```

**Error Responses:**

- `400 Bad Request` - Invalid username, max clicks, or expiry date
  ```json
  "Username must be 1-50 alphanumeric characters only."
  ```

### View Secret

Accesses a secret link and displays the message if valid.

**Endpoint:** `GET /supersecret/{token}`

**Response:** HTML page with one of:
- **Success (first valid visit):** "You have found the secret, {username}!"
- **Failure (invalid/expired/exhausted):** "There are no secrets here"

**Cache Headers:**
```
Cache-Control: no-store
```

---

## Examples

### Example 1: Single-Use Link (Default)

**Request:**
```bash
curl -X POST https://localhost:7001/api/links \
  -H "Content-Type: application/json" \
  -d '{
    "username": "Alice"
  }'
```

**Response:**
```json
{
  "url": "https://localhost:7001/supersecret/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Behavior:**
- First visit: Shows "You have found the secret, Alice!"
- Second visit: Shows "There are no secrets here"

---

### Example 2: Multi-Use Link (3 clicks)

**Request:**
```bash
curl -X POST https://localhost:7001/api/links \
  -H "Content-Type: application/json" \
  -d '{
    "username": "Bob",
    "max": 3
  }'
```

**Response:**
```json
{
  "url": "https://localhost:7001/supersecret/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Behavior:**
- Visits 1-3: Shows "You have found the secret, Bob!"
- Visit 4+: Shows "There are no secrets here"

---

### Example 3: Link with Expiry

**Request:**
```bash
curl -X POST https://localhost:7001/api/links \
  -H "Content-Type: application/json" \
  -d '{
    "username": "Charlie",
    "max": 10,
    "expiresAt": "2024-12-31T23:59:59Z"
  }'
```

**Response:**
```json
{
  "url": "https://localhost:7001/supersecret/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Behavior:**
- Valid until December 31, 2024, or 10 clicks (whichever comes first)
- After expiry: Shows "There are no secrets here"

---

### Example 4: PowerShell

```powershell
$body = @{
    username = "David"
    max = 5
    expiresAt = (Get-Date).AddDays(7).ToString("o")
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "https://localhost:7001/api/links" `
    -ContentType "application/json" `
-Body $body
```

---

### Example 5: JavaScript (Browser)

```javascript
async function createLink() {
    const response = await fetch('https://localhost:7001/api/links', {
     method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            username: 'Eve',
       max: 1,
       expiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString()
        })
    });
    
    const data = await response.json();
    console.log('Generated URL:', data.url);
}
```

---

### Example 6: Python

```python
import requests
from datetime import datetime, timedelta

url = "https://localhost:7001/api/links"
payload = {
    "username": "Frank",
    "max": 2,
  "expiresAt": (datetime.utcnow() + timedelta(days=30)).isoformat() + "Z"
}

response = requests.post(url, json=payload, verify=False)  # verify=False for dev only
print(response.json())
```

---

## Validation Rules

### Username
- **Required:** Yes
- **Min Length:** 1 character
- **Max Length:** 50 characters
- **Allowed Characters:** A-Z, a-z, 0-9 only (no spaces or special characters)
- **Examples:**
  - ? Valid: `John`, `Alice123`, `Bob2024`
  - ? Invalid: `John Doe`, `Alice@123`, `Bob!`, `This_Is_Too_Long_Username_That_Exceeds_Fifty_Characters`

### Max Clicks
- **Required:** No (defaults to 1)
- **Min Value:** 1
- **Max Value:** No hard limit (but consider practical limits)
- **Examples:**
  - ? Valid: `1`, `5`, `100`
  - ? Invalid: `0`, `-1`

### Expiry Date
- **Required:** No (no expiration if omitted)
- **Format:** ISO 8601 datetime string
- **Timezone:** UTC recommended
- **Validation:** Must be in the future
- **Examples:**
  - ? Valid: `2024-12-31T23:59:59Z`, `2025-01-15T12:00:00.000Z`
  - ? Invalid: `2020-01-01T00:00:00Z` (past date), `12/31/2024` (wrong format)

---

## Error Codes

| Status Code | Description | Example Message |
|-------------|-------------|-----------------|
| 200 OK | Link created successfully | `{ "url": "..." }` |
| 400 Bad Request | Invalid request parameters | `"Username must be 1-50 alphanumeric characters only."` |
| 400 Bad Request | Invalid max clicks | `"Max clicks must be at least 1."` |
| 400 Bad Request | Invalid expiry date | `"Expiry date must be in the future."` |
| 500 Internal Server Error | Server error (e.g., database connection) | `"An error occurred"` |

---

## Token Format

Tokens use HMAC-SHA256 signed JWT format (compact serialization):

```
{header}.{payload}.{signature}
```

### Header
```json
{
  "alg": "HS256",
  "typ": "JWT"
}
```

### Payload (Claims)
```json
{
  "sub": "Alice",           // Username
  "jti": "abcd1234...",     // Unique ID (26 chars)
  "max": 3,           // Max clicks
  "exp": 1735689599,        // Unix timestamp (optional)
  "ver": 1   // Token version
}
```

### Security Notes

- Tokens are **signed but not encrypted** - do not include sensitive data in the username
- HMAC signature prevents token tampering
- Expiry is validated on every access
- Click counts are tracked server-side in the database
- Constant-time signature comparison prevents timing attacks

---

## Rate Limiting

Currently, there is **no rate limiting** implemented. For production use, consider adding:
- Rate limiting middleware (e.g., `AspNetCoreRateLimit`)
- API keys for authenticated access
- CAPTCHA for the admin UI

---

## Testing

### Test Single-Use Link

```bash
# Create link
URL=$(curl -s -X POST https://localhost:7001/api/links \
  -H "Content-Type: application/json" \
  -d '{"username":"TestUser"}' | jq -r '.url')

echo "Created: $URL"

# Visit once (should succeed)
curl -s "$URL" | grep "You have found the secret"

# Visit again (should fail)
curl -s "$URL" | grep "There are no secrets here"
```

### Test Multi-Use Link

```bash
# Create link with 3 uses
URL=$(curl -s -X POST https://localhost:7001/api/links \
  -H "Content-Type: application/json" \
  -d '{"username":"TestUser","max":3}' | jq -r '.url')

# Visit 3 times
for i in {1..3}; do
  echo "Visit $i:"
  curl -s "$URL" | grep -o "You have found the secret\|There are no secrets here"
done

# 4th visit should fail
echo "Visit 4:"
curl -s "$URL" | grep -o "There are no secrets here"
```

---

## Database Queries

### View All Active Links

```sql
-- Single-use links
SELECT Jti, CreatedAt, ExpiresAt 
FROM dbo.SingleUseLinks 
WHERE ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME()
ORDER BY CreatedAt DESC;

-- Multi-use links
SELECT Jti, ClicksLeft, CreatedAt, ExpiresAt 
FROM dbo.MultiUseLinks 
WHERE ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME()
ORDER BY CreatedAt DESC;
```

### Manual Cleanup

```sql
-- Remove all expired links
DELETE FROM dbo.SingleUseLinks WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
DELETE FROM dbo.MultiUseLinks WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();

-- Remove all links (reset database)
TRUNCATE TABLE dbo.SingleUseLinks;
TRUNCATE TABLE dbo.MultiUseLinks;
```
