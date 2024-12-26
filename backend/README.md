# Object Detection Backend Service

This service provides an API endpoint for analyzing images using Google Cloud Platform's Vertex AI Vision API. The service is built with FastAPI and deployed on Google Cloud Run.

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
gcloud config set project $PROJECT_ID

# Enable required APIs
gcloud services enable \
    run.googleapis.com \
    aiplatform.googleapis.com \
    storage.googleapis.com \
    artifactregistry.googleapis.com
```

### 2. Create Cloud Storage Bucket

```bash
# Create a bucket for storing images
gcloud storage buckets create gs://$BUCKET_NAME --location=asia-south1
```

### 3. Local Development Setup

```bash
# Create and activate virtual environment
python -m venv .venv
source .venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt

# Set environment variables
export PROJECT_ID=joi-reality
export BUCKET_NAME=object-detection-images
export GOOGLE_APPLICATION_CREDENTIALS=../../secrets/GCP/service-account-key.json
```

### 4. Local Testing

```bash
# Run the FastAPI application locally
uvicorn main:app --reload --port 8080 --host 0.0.0.0
```

Visit http://localhost:8080/docs to see the Swagger UI and test the API.

## Deployment to Cloud Run

### 1. Build and Deploy

```bash
# Build and push the container using Cloud Build
gcloud builds submit --tag gcr.io/$PROJECT_ID/object-detection-api

# Deploy to Cloud Run
gcloud run deploy object-detection-api \
    --image gcr.io/$PROJECT_ID/object-detection-api \
    --platform managed \
    --region asia-south1 \
    --allow-unauthenticated \
    --service-account=$SERVICE_ACCOUNT_EMAIL \
    --set-env-vars="PROJECT_ID=$PROJECT_ID,BUCKET_NAME=$BUCKET_NAME,LOCATION=us-central1"

# Note: Make sure your service account has the following roles:
# - roles/aiplatform.user
# - roles/storage.objectViewer
# - roles/storage.objectCreator
```

### 2. Managing Deployments

```bash
# List container images and tags
gcloud container images list-tags gcr.io/$PROJECT_ID/object-detection-api --format='get(tags)'

# List Cloud Run revisions
gcloud run revisions list --service object-detection-api --region asia-south1

# View revision traffic allocation
gcloud run services describe object-detection-api --region asia-south1

# Rollback to previous revision if needed (replace REVISION_NAME)
gcloud run services update-traffic object-detection-api \
    --region asia-south1 \
    --to-revisions=REVISION_NAME=100

# Clean up old container images (keeps the latest one)
gcloud container images list-tags gcr.io/$PROJECT_ID/object-detection-api \
    --format="get(digest)" \
    --filter="NOT tags:latest" | \
    while read digest; do \
        gcloud container images delete -q --force-delete-tags "gcr.io/$PROJECT_ID/object-detection-api@$digest"; \
    done
```

You can also manage deployments in the GCP Console:
1. Container Registry: https://console.cloud.google.com/gcr/images
2. Cloud Run Revisions: https://console.cloud.google.com/run/detail/asia-south1/object-detection-api/revisions

### 3. Get the Service URL

```bash
# Get the service URL and set it as an environment variable
    export SERVICE_URL=$(gcloud run services describe object-detection-api \
        --platform managed \
        --region asia-south1 \
        --format 'value(status.url)' | sed 's/https:\/\///')
```

## Testing the Deployed API

### Using OpenAPI Docs

Visit https://$SERVICE_URL/docs to see the OpenAPI documentation and test the API.

### Using cURL

```bash
# Test health endpoint
curl https://$SERVICE_URL/health

# Test image analysis (replace path/to/image.jpg with your image path)
curl -X POST \
    -F "file=@path/to/image.jpg" \
    https://$SERVICE_URL/analyze

# Test image analysis with Google Search enabled (default)
curl -X POST \
    -F "file=@path/to/image.jpg" \
    -F "enable_google_search=true" \
    https://$SERVICE_URL/analyze

# Test image analysis with Google Search disabled
curl -X POST \
    -F "file=@path/to/image.jpg" \
    -F "enable_google_search=false" \
    https://$SERVICE_URL/analyze

```

### Using Python

```python
import requests

# Replace with your deployed service URL
url = f"https://{SERVICE_URL}/analyze"

# Replace with your image path
image_path = "path/to/image.jpg"

# With Google Search enabled (default)
with open(image_path, "rb") as image_file:
    files = {"file": ("image.jpg", image_file, "image/jpeg")}
    data = {"enable_google_search": "true"}
    response = requests.post(url, files=files, data=data)

print(response.json())

# With Google Search disabled
with open(image_path, "rb") as image_file:
    files = {"file": ("image.jpg", image_file, "image/jpeg")}
    data = {"enable_google_search": "false"}
    response = requests.post(url, files=files, data=data)

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
        "model": "gemini-pro-vision-001",
        "processing_time": 1.23,
        "timestamp": 1234567890.123,
        "file_id": "uuid",
        "storage_path": "string",
        "upload_time": 0.5,
        "analysis_time": 0.7,
        "total_processing_time": 1.2,
        "google_search_enabled": true
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