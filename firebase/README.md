# Shoe Detection and Foot Measurement Firebase Functions

This project contains two Firebase Functions that use Genkit and Gemini Pro Vision:
1. Shoe Detection - Identifies shoes in images and matches them against a Firestore database
2. Foot Measurement Validation - Validates if feet are ready for measurement (bare feet, with socks, or with footwear)

## Project Structure
```
firebase/functions/
├── src/
│   ├── services/
│   │   ├── shoe-detection/     # Shoe detection service
│   │   └── foot-measurement/   # Foot measurement validation
│   ├── utils/                  # Shared utilities
│   ├── tests/                  # Test files
│   └── index.ts               # Main entry point
```

## Prerequisites

- Node.js v20 or later
- Firebase CLI
- Firebase project with Blaze plan enabled
- Google AI API key
- Firestore collection named "shoes" with shoe documents containing "name" field

## Setup

1. Install dependencies:
```bash
cd functions
npm install
```

2. Set up Firebase project:
```bash
firebase login
firebase use YOUR_PROJECT_ID
```

3. Set up secrets:
```bash
firebase functions:secrets:set GOOGLE_GENAI_API_KEY
```

4. For testing, set up test images:
```bash
mkdir -p test-images
# Add test images:
# - test-shoe.jpg         # For shoe detection
# - feet-with-shoes.jpg   # For foot measurement
# - feet-with-socks.jpg   # For foot measurement
# - bare-feet.jpg         # For foot measurement
```

5. For testing with Firestore, get your service account key:
- Go to Firebase Console > Project Settings > Service Accounts
- Click "Generate New Private Key"
- Save as `functions/service-account.json`

6. Deploy:
```bash
firebase deploy --only functions
```

## Testing

### Unit Tests
Run unit tests that mock the Firebase Functions environment:
```bash
cd functions
npm run test:unit
```

### Integration Tests
There are two ways to run integration tests:

1. Against deployed functions (requires deployment first):
```bash
npm run test:integration
```

2. Against local emulator (requires emulator running):
```bash
# In one terminal:
npm run serve

# In another terminal:
npm run test:integration:emulator
```

### All Tests
Run both unit and integration tests:
```bash
npm test
```

### Using Firebase CLI:
```bash
firebase functions:shell
```

Then call the functions:
```js
// Test shoe detection
detectShoe({data: "BASE64_ENCODED_IMAGE"})

// Test foot measurement validation
validateFootMeasurement({data: "BASE64_ENCODED_IMAGE"})
```

### Using the Genkit Developer UI:
```bash
cd functions
npx genkit start -o -- npx tsx --watch src/index.ts
```

## Response Formats

### Shoe Detection
Returns one of:
- Shoe name from database if matched
- "UNKNOWN_SHOE" if a shoe is detected but not in database
- "SHOE_NOT_FOUND" if no shoe is detected

### Foot Measurement Validation
Returns one of:
- "WEARING_FOOTWEAR" if wearing shoes, sandals, or any footwear
- "WEARING_SOCKS" if wearing socks but no footwear
- "BARE_FEET" if feet are bare

## Unity Integration

```csharp
using System;
using System.Threading.Tasks;
using Firebase.Functions;
using UnityEngine;

public class FootMeasurementManager : MonoBehaviour
{
    private FirebaseFunctions functions;

    void Start()
    {
        functions = FirebaseFunctions.DefaultInstance;
    }

    public async Task<string> ValidateFootMeasurement(byte[] imageBytes)
    {
        try
        {
            // Convert image to base64
            string base64Image = Convert.ToBase64String(imageBytes);
            
            // Create data object
            var data = new {
                data = base64Image
            };

            // Call the function
            var function = functions.GetHttpsCallable("validateFootMeasurement");
            var result = await function.CallAsync(data);
            
            // Return the status
            return result.Data.ToString();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error validating foot measurement: {e.Message}");
            throw;
        }
    }
}
``` 