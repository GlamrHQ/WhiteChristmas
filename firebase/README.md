# Firebase Functions Project

This project contains multiple Firebase Cloud Functions written in Python. Each function is isolated with its own dependencies and virtual environment.

## Project Structure

```
firebase/
├── functions/
│   ├── foot_measurement/         # Foot measurement function
│   │   ├── main.py              # Function implementation
│   │   ├── requirements.txt     # Dependencies
│   │   ├── .python-version      # Python version specification
│   │   └── tests/               # Unit tests
│   │       └── test_measurement.py
├── scripts/
│   ├── deploy.sh                # Deployment script
│   └── run_tests.sh            # Test runner script
└── firebase.json                # Firebase configuration
```

## Functions

### Foot Measurement
- Endpoint that processes multiple foot images to predict foot measurements
- Uses computer vision and ArUco markers for accurate measurements
- Returns foot length, width, and confidence metrics
- Deployed to Mumbai region (asia-south1)

#### Function URLs
After deployment, your functions will be available at:
```
# Main endpoint (Callable)
https://asia-south1-<PROJECT_ID>.cloudfunctions.net/measure_feet

# Health check endpoint (HTTP)
https://asia-south1-<PROJECT_ID>.cloudfunctions.net/health
```

To get your specific function URLs, run:
```bash
firebase functions:list
```

#### Sample Requests

1. Health Check (HTTP GET):
```bash
# Using curl
curl https://asia-south1-<PROJECT_ID>.cloudfunctions.net/health

# Using wget
wget https://asia-south1-<PROJECT_ID>.cloudfunctions.net/health

# Using httpie
http GET https://asia-south1-<PROJECT_ID>.cloudfunctions.net/health
```

Response:
```json
{
    "status": "healthy",
    "timestamp": "2024-12-31T03:45:00.000000",
    "service": "foot_measurement"
}
```

2. Foot Measurement (Callable):
```bash
curl -X POST -H "Content-Type: application/json" \
     -d '{"images": ["base64_encoded_image_1", "base64_encoded_image_2"]}' \
     https://asia-south1-<PROJECT_ID>.cloudfunctions.net/measure_feet
```

## Development Requirements

- Python 3.10
- Firebase CLI
- Conda (for environment management)
- Virtual environment support (`python -m venv`)

## Dependencies

Each function has its own `requirements.txt` file specifying its dependencies:

### Dependencies (`requirements.txt`)
```
numpy==2.2.1
opencv-python-headless==4.10.0.84
pyzbar==0.1.8
firebase-functions==0.1.0
firebase-admin>=6.0.0             # Required by firebase-functions 0.1.0
functions-framework==3.4.0
pytest==7.0.0
```

## Deployment

The project uses isolated environments for each function. The `deploy.sh` script handles:
1. Creating function-specific Conda environments with Python 3.10
2. Setting up Firebase-compatible virtual environments
3. Installing dependencies
4. Deploying to Firebase

### Deploy Specific Function
```bash
# Use FUNCTIONS_DISCOVERY_TIMEOUT to prevent timeout during deployment
FUNCTIONS_DISCOVERY_TIMEOUT=60 ./scripts/deploy.sh foot_measurement
```

### Deploy All Functions
```bash
FUNCTIONS_DISCOVERY_TIMEOUT=60 ./scripts/deploy.sh
```

## Optimization Notes

The project is optimized for minimal deployment size and cost:

1. **Package Management**:
   - Uses specific package versions to avoid conflicts
   - Maintains compatible package versions
   - Uses pip's no-cache installation to reduce deployment size

2. **Deployment Optimization**:
   - Cleans pip cache during deployment
   - Uses no-cache installations
   - Excludes unnecessary files during deployment
   - Deploys to Mumbai region for better latency in India

3. **Environment Management**:
   - Uses Conda for consistent Python version management
   - Creates isolated environments per function
   - Uses Firebase-compatible virtual environment structure

## Size Considerations
Firebase Functions has the following limits:
- Maximum deployment package size: 500MB unzipped
- Maximum zipped size: 100MB
- Individual file size limit: 100MB

Current optimizations keep the deployment well within these limits while maintaining full functionality. 

## Testing

The project includes unit tests for each function. Tests are run in isolated environments to ensure consistency.

### Running Tests

The `run_tests.sh` script handles:
1. Creating function-specific test environments with Python 3.10
2. Installing all required dependencies
3. Running pytest with verbose output
4. Cleaning up environments after tests

#### Run Tests for a Specific Function
```bash
./scripts/run_tests.sh foot_measurement
```

#### Run Tests for All Functions
```bash
./scripts/run_tests.sh
```

### Test Structure
- Tests are located in the `tests` directory of each function
- Uses pytest for test running and assertions
- Includes fixtures for common test data
- Tests core functionality in isolation 