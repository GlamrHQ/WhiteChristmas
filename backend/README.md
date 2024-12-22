# Object Detection Backend Service

This service provides an API endpoint for analyzing images using Google Cloud Platform's Gemini Vision API. The service is built with FastAPI and deployed on Google Cloud Run.

## Prerequisites

1. Google Cloud Platform Account
2. Google Cloud CLI installed
3. Python 3.11+ installed locally for testing
4. Docker installed locally

## Setup Instructions

### 1. Initial GCP Setup

```bash
# Install Google Cloud CLI
# Mac
brew install google-cloud-sdk

# Login to GCP
gcloud auth login

# Set your project
gcloud config set project YOUR_PROJECT_ID

# Enable required APIs
gcloud services enable \
    run.googleapis.com \
    aiplatform.googleapis.com \
    storage.googleapis.com
```

### 2. Create Cloud Storage Bucket

```bash
# Create a bucket for storing images
gcloud storage buckets create gs://object-detection-images --location=asia-south1
```

### 3. Local Development Setup

```bash
# Create and activate virtual environment
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt

# Set environment variables
export PROJECT_ID=your-project-id
export BUCKET_NAME=object-detection-images
export GOOGLE_APPLICATION_CREDENTIALS=path/to/your/service-account-key.json
```

### 4. Local Testing

```bash
# Run the FastAPI application locally
uvicorn main:app --reload --port 8080
```

Visit http://localhost:8080/docs to see the Swagger UI and test the API.

## Deployment to Cloud Run

### 1. Build and Deploy

```bash
# Build and push the container
gcloud builds submit --tag gcr.io/YOUR_PROJECT_ID/object-detection-api

# Deploy to Cloud Run
gcloud run deploy object-detection-api \
    --image gcr.io/YOUR_PROJECT_ID/object-detection-api \
    --platform managed \
    --region asia-south1 \
    --allow-unauthenticated \
    --set-env-vars="PROJECT_ID=YOUR_PROJECT_ID,BUCKET_NAME=object-detection-images"
```

### 2. Get the Service URL

```bash
gcloud run services describe object-detection-api \
    --platform managed \
    --region asia-south1 \
    --format 'value(status.url)'
```

## Testing the Deployed API

### Using cURL

```bash
# Test health endpoint
curl https://YOUR-CLOUD-RUN-URL/health

# Test image analysis (replace path/to/image.jpg with your image path)
curl -X POST \
    -F "file=@path/to/image.jpg" \
    https://YOUR-CLOUD-RUN-URL/analyze
```

### Using Python

```python
import requests

# Replace with your deployed service URL
url = "https://YOUR-CLOUD-RUN-URL/analyze"

# Replace with your image path
image_path = "path/to/image.jpg"

with open(image_path, "rb") as image_file:
    files = {"file": ("image.jpg", image_file, "image/jpeg")}
    response = requests.post(url, files=files)

print(response.json())
```

## Monitoring and Logging

### View Logs

```bash
# View Cloud Run logs
gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=object-detection-api" --limit 50

# Stream logs
gcloud logging tail "resource.type=cloud_run_revision AND resource.labels.service_name=object-detection-api"
```

### Monitor Performance

1. Visit the Cloud Run console: https://console.cloud.google.com/run
2. Click on your service (object-detection-api)
3. View metrics in the "METRICS" tab

## API Response Format

```json
{
    "status": "success",
    "data": {
        "main_object": "string",
        "confidence": 0.95,
        "attributes": {
            "color": "string",
            "size": "string",
            "condition": "string",
            "distinguishing_features": ["string"]
        },
        "context": "string",
        "visible_text": "string",
        "brand": "string"
    },
    "metadata": {
        "model": "gemini-pro-vision",
        "processing_time": 1.23,
        "timestamp": 1234567890.123,
        "file_id": "uuid",
        "storage_path": "string",
        "upload_time": 0.5,
        "analysis_time": 0.7,
        "total_processing_time": 1.2
    }
}
```

## Error Handling

The API returns appropriate HTTP status codes:
- 200: Successful analysis
- 400: Bad request (invalid file format, etc.)
- 500: Server error (processing failed, etc.)

## Security Considerations

1. The service is deployed with HTTPS enabled by default
2. File uploads are limited to images
3. Each upload generates a unique ID
4. All API calls are logged for auditing
5. Cloud Run provides automatic scaling and DDoS protection

## Cost Optimization

1. Images are processed asynchronously
2. Gemini API calls are optimized for performance
3. Cloud Run only charges for actual usage
4. Consider implementing a cleanup job for old images in Cloud Storage

## Troubleshooting

1. **API Returns 500 Error**
   - Check Cloud Run logs
   - Verify GCP service account permissions
   - Ensure Gemini API is enabled

2. **Slow Response Times**
   - Check Cloud Run instance configuration
   - Monitor Cloud Storage performance
   - Review Gemini API quotas

3. **Image Upload Fails**
   - Verify file size limits
   - Check file format support
   - Ensure Cloud Storage permissions 