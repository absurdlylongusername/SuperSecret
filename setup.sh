#!/bin/bash

# Setup script for SuperSecret application

echo "SuperSecret - Setup Script"
echo "=========================="
echo ""

# Check if .NET is installed
if ! command -v dotnet &> /dev/null
then
    echo "Error: .NET SDK is not installed. Please install .NET 9 SDK first."
    exit 1
fi

echo "Step 1: Setting up User Secrets..."
cd SuperSecret

# Generate a random 32-character signing key
SIGNING_KEY=$(openssl rand -base64 32 | tr -d '\n')

# Set user secrets
dotnet user-secrets set "TokenSigningKey" "$SIGNING_KEY"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=SuperSecretDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"

echo "? User secrets configured"
echo ""

echo "Step 2: Starting SQL Server (Docker)..."
cd ..
docker-compose up -d

echo "Waiting for SQL Server to be ready (30 seconds)..."
sleep 30

echo "? SQL Server started"
echo ""

echo "Step 3: Creating database schema..."
# Check if sqlcmd is available
if command -v sqlcmd &> /dev/null
then
    sqlcmd -S localhost,1433 -U sa -P "YourStrong@Passw0rd" -i SuperSecret/DatabaseSchema.sql -C
    echo "? Database schema created"
else
echo "? sqlcmd not found. Please run SuperSecret/DatabaseSchema.sql manually using:"
    echo "  - SQL Server Management Studio"
    echo "  - Azure Data Studio"
    echo "  - Or install sqlcmd tools"
fi

echo ""
echo "=========================="
echo "Setup Complete!"
echo "=========================="
echo ""
echo "To start the application:"
echo "  cd SuperSecret"
echo "  dotnet run"
echo ""
echo "Then navigate to:"
echo "  - Admin UI: https://localhost:7xxx/admin"
echo "  - API: POST https://localhost:7xxx/api/links"
echo ""
