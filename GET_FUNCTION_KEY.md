# Function Key Information

## Quick Answer

**No function key needed!** The HTTP function is configured with `AuthorizationLevel.Anonymous`, so you can call it directly without any authentication.

## Usage

### Direct Calls (No Key Needed!)

Simply call the function directly:

```bash
# Basic call with defaults
curl "http://localhost:7071/api/PriceDiffHttpFunction"

# With custom parameters
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=ETHUSDT&periods=30,60,90"

# POST request with JSON body
curl -X POST "http://localhost:7071/api/PriceDiffHttpFunction" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "BTCUSDT", "periods": "60,90"}'
```

### Example Output from `func start`

When you run `func start`, you'll see:

```
Functions:
    PriceDiffHttpFunction: [GET,POST] http://localhost:7071/api/PriceDiffHttpFunction

Host started
```

Notice: **No `?code=...` in the URL** - that's because authentication is disabled!

## Production Security

⚠️ **Important**: For production deployments, you should enable authentication:

1. **Change Authorization Level** in `CryptoDiffs/PriceDiffHttpFunction.cs`:
   ```csharp
   // Change from:
   [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]
   
   // To:
   [HttpTrigger(AuthorizationLevel.Function, "get", "post")]
   ```

2. **Get Function Key** from Azure Portal:
   - Azure Portal → Your Function App → Functions → PriceDiffHttpFunction → Function Keys

3. **Use Key in Requests**:
   ```bash
   curl "https://your-function-app.azurewebsites.net/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY"
   ```

**Alternative Security Options**:
- Azure App Service Authentication
- API Management with authentication
- VNet integration for network restrictions
