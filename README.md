# SuperSecret Token Service

A small application for generating single or multi-use secret links.

When a user clicks a link, they see a personalised message (`You have found the secret, {username}!`).
If the link has expired or is invalid they see a default messsage `There are no secrets here`.

## Features

- Generate secret links via:
    - Web admin UI (`/Admin`)
    - JSON API (`/api/links`)
- Personalised messages per user (required `username`)
- Single-use or multi-use links (max click count)
- Time-based expiry (TTL or absolute expiry time)
- Cryptographically signed tokens with integrity protection
- SQL Server backing store for token tracking and expiry clean-up
- Background process to remove expired tokens
- Unit, integration and UI tests (Playwright)



## Tech Stack

- **Backend:** .NET 9, ASP.NET Core Razor Pages + minimal API
- **Data:** SQL Server 2022 + Dapper
- **Security:** HMAC-signed JWT-style tokens, ULIDs for token IDs
- **Testing:** NUnit (unit + integration tests), Playwright (UI tests)
- **API exploration:** [Scalar](https://github.com/scalar/scalar) OpenAPI UI


## Domain Overview

A secret link encapsulates:

- `username` – required; used to personalise the message
- `maxClicks` – optional; number of times a link can be used
- `expiry` – optional; a date time

Secret links are in the form of:

````
    {domain}/supersecret/{token}
````    


## Architecture

### Application

The whole thing is one application.

The `/Admin` page is a Razor Pages frontend, and the `/api/links` endpoint is a minimal API.
Both the API and UI use the same services and validation logic.

### Token Generation and Validation

Tokens store as much relevant data as possible within themselves so it doesn't need to be stored in the DB.

- **JWT Token Claims Structure**:

    - `sub`: username
    - `jti`: unique token id (ULID)
    - `exp`: expiry
    - `max`: max allowed clicks
    - `ver`: token schema version

Plain token data is stored in this format, and is signed and converted into a JSON token.

Tokens are in this form: `base64url(header) . base64url(payload) . base64url(HMAC_SHA256(header.payload, secretKey))`

Where `header` is of the form 
````json
{ 
  "alg": "HS256",
  "typ": "JWT"
}
````

And `payload` is just the JSON of the claims stated above.

Tokens are signed with a secret key using the HMAC SHA256 algorithm. This signing is used for validation to ensure tokens are not tampered with.

Every token has a unique ID `jti`, which is a ULID.

Tokens have a maximum TTL of 30 days.

### Database

I used SQL Server + Dapper for database operations.

There are two types of tokens: Single Use, and Multi Use; both of which are stored in different tables.

Single use tokens store only the token ID and the expiry date; multi use store the same but also the remaining clicks on the link/token.
I considered using a single table for both types, but having a separate single use table makes extraction of single use tokens simpler (they can be deleted on access without checking remaining clicks, whereas a single table for both means checking remaining clicks for single use tokens).

Token IDs are stored as 16 binary bytes (converting the ULID to 16 bytes) instead of CHAR(26), for maximum storage efficiency.

A background service runs periodically to delete expired tokens in both tables.

## Storage and Speed Performance Considerations

The service is designed with a target of around 1M new links per month.

This calculates to \<1 link per second on average, so throughput is not a concern.
I mainly optimised for storage, by storing as little as data as possible for operation.

Assuming ~200 bytes per token row and 30 days max expiry, with background cleanup running periodically, total storage will not exceed 200-300MiB at any point. 

- Throughput is relatively modest (&l 1 link per second on average), so CPU/latency is not the primary concern.
- Storage is the main factor:

    - Assuming ~200 bytes per token record and a 30-day maximum TTL, total storage remains well within a few hundred MiB over time.
- Background cleanup keeps the active dataset small by removing expired tokens regularly.

## Testing

I developed this using TDD, so all the core functionality is tested with robust unit and integration tests.

Token generation, validation, database interactions, UI interactions are all adequately tested using NUnit and Playwright.

# How to run it

**Prerequisites**

- .NET 9
- Visual Studio 2022
  - Ensure the standard ASP.NET components are installed (in VS -> Tools -> Get Tools and Features)
  - Ensure "Data storage and processing" toolset is installed
- SQL Server 2022


After cloning the repository, open the solution Visual Studio and publish the SQL project to your SQL database

- Right click `SuperSecretDatabase` in Solution Explorer -> `Publish`
- Click `Edit..`
- Select a DB connection, or if none appear go to the Browse tab and select one there
- Enter database name (This database name must match the database in your connection string in `appsettings.json`)

This will create the database schema for the application in your SQL Server database connection.

Update the connection string in `appsettings.json` (or `appsettings.Development.json`, or set it in user secrets) to point to the newly created database.
Set a Token Signing key in the appsettings or in user secrets.


After that you can run the app.

## Scalar API

This uses [Scalar](https://github.com/scalar/scalar), an OpenAPI browser client for querying the API. You can access it at `{domain}/scalar`.


## Running the tests

Before running tests you must set the connection string in `testsettings.json`.

Ideally, this will point to a different database than the one in appsettings.json (you can create a new one by following the publish steps above but giving a different database name).

For the Playwright tests you will also need to set the `BaseUrl` to point to an already running instance of the application.
