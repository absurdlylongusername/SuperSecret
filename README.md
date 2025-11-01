# SuperSecret Token Service

Bonjour! This is my submission for the technical test for KYC360.

## The Task

Design an application that generates secret links. When a user clicks the link, it will display a secret message for that user (`You have found the secret, {username}!`), but subsequent clicks will show a message saying the link is not available (`There are no secrets here`)

Extra functionality: 
- Links can be clicked X number of times before expiring
- Links can expire after a period of time


Links can be created by an admin page where they can enter the username for the message, as well as number of clicks and expiry date.

Links can also be created by an API endpoint that communicates using JSON.

For all link creation methods, the username is required, but other parameters are optional.

# My Solution

My solution is a Razor Pages application with a minimal API.

It has an Admin page (`/Admin`) for generating links where you can enter a username, max clicks, and the link's Time To Live (TTL) in seconds, minutes, hours and/or days.

It exposes an API endpoint for link generation (`/api/links`) that has the same parameters as the admin page, but the expiry date is specified as a DateTime instead of separate parameters for seconds, minutes, etc.

Generated links are in the form of `{domain}/supersecret/{token}`.

Each token is a JWT-like token that is cryptographically signed and contains all relevant information about the link (username, expiry, max clicks, etc.)

It uses a SQL Server database for storing token information to keep track of existing tokens and expiry.


## Token Generation and Validation

Tokens stored as much relevant data as possible within themselves so it doesn't need to be stored in a database.

- **Token Claims Structure**:

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

Tokens are signed with a secret using the HMAC SHA256 algorithm. This signing is used for validation to ensure tokens are not tampered with.

Every token has a unique ID `jti`, which is a ULID.

Tokens have a maximum TTL of 30 days.


## Database

A SQL Server database is used to store information on existing tokens.

When a token is created from a request, it is stored in the database. 

There's two types of tokens: single use and multi use, each of which are stored in different tables.

Single use tokens store only the token ID and the expiry date; multi use store the same but also the remaining clicks on the link/token.
I considered using a single table for both types, but having a separate single use table makes extraction of single use tokens simpler (they can be deleted on access without checking remaining clicks, whereas a single table for both means checking remaining clicks for single use tokens).

Token IDs are stored as 16 binary bytes instead of CHAR(26), for maximum storage efficiency.

A background service runs periodically to delete expired tokens in both tables.

## Storage and Speed Performance Considerations

The brief states expected usage is 1M+ links a month, which calculates to <1 link per second. This is extremely low throughput, so speed is not really a concern based on the brief.

The main concern is storage. 1M links a month, assuming a generous maximum of 200B of storage per link, the storage upper bound would never exceed 200 MiB if we have a max link TTL of 30 days.

## Tests

I initially developed this by just writing the functionality, but switched to using a TDD approach midway due to the fact I encountered a bug that could be captured and reproduced within a test, and it occurred to me that it would make sense to specify my desired functionality in the form of tests and fix bugs that way, rather than doing it manually.

This helped me avoid the situation of writing a bunch of code, running it, finding out it doesn't work, and spending ages fixing an obscure bug: a scenario which I've experienced many a time. Since the general API and interface of the program had already been specified, I was able to write tests first and then focus on refining implementation afterwards.

As a result, almost all of the code is unit and integration tested

# How to run it

**Prerequisites**

- .NET 9
- Visual Studio 2022
  - Ensure the standard ASP.NET components are installed (in VS -> Tools -> Get Tools and Features)
  - Ensure "Data storage and processing" toolset is installed
- SQL Server 2022


After cloning the repository, open the solution Visual Studio and publish the SQL project to your SQL database

- Right click SuperSecretDatabase in Solution Explorer -> Publish
- Click Edit..
- Select a DB connection, or if none appear go to the Browse tab and select one there
- Enter database name (This database name must matc the database in your connection string in appsettings.json)

This will create the database schema for the application in your SQL Server database connection.

Update the connection string in `appsettings.json` (or `appsettings.Development.json`, or set it in user secrets) to point to the newly created database.
Set a Token Signing key in the appsettings or in user secrets.


After that you can run the app. 


## Running the tests

Before running tests you must set the connection string in `testsettings.json`.

Ideally, this will point to a different database than the one in appsettings.json (you can create a new one by following the publish steps above but giving a different database name).

For the Playwright tests you will also need to set the `BaseUrl` to point to an already running instance of the application.