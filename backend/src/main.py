from fastapi import FastAPI, UploadFile, HTTPException
from fastapi.responses import JSONResponse
from google.cloud import storage
from google.cloud import aiplatform
import vertexai
from vertexai.generative_models import GenerativeModel, Part
import time
import logging
import uuid
import os
from typing import Dict
import json

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="Object Detection API")

# Initialize GCP clients
storage_client = storage.Client()
BUCKET_NAME = os.getenv("BUCKET_NAME", "object-detection-images")

# Initialize Vertex AI
PROJECT_ID = os.getenv("PROJECT_ID")
LOCATION = os.getenv("LOCATION", "asia-south1")
vertexai.init(project=PROJECT_ID, location=LOCATION)


def get_gemini_response(image_path: str) -> Dict:
    """Get Gemini's analysis of the image."""
    start_time = time.time()

    try:
        model = GenerativeModel("gemini-pro-vision")

        # Load image from GCS
        bucket = storage_client.bucket(BUCKET_NAME)
        blob = bucket.blob(image_path)
        image_content = blob.download_as_bytes()

        # Prepare the image part
        image_part = Part.from_data(data=image_content, mime_type="image/jpeg")

        # System prompt with function calling
        prompt = """You are an expert computer vision system analyzing images.
        Your task is to provide detailed information about objects in the image.
        Focus on:
        - Main object identification
        - Visual characteristics (color, shape, size)
        - State/condition
        - Context/environment
        - Any text visible
        - Brand identification if applicable
        
        Format your response using the provided function structure.
        Be precise and confident in your observations."""

        # Define response structure
        response_format = {
            "name": "analyze_image",
            "description": "Analyze the contents of an image",
            "parameters": {
                "type": "object",
                "properties": {
                    "main_object": {
                        "type": "string",
                        "description": "Primary object identified",
                    },
                    "confidence": {
                        "type": "number",
                        "description": "Confidence score 0-1",
                    },
                    "attributes": {
                        "type": "object",
                        "properties": {
                            "color": {"type": "string"},
                            "size": {"type": "string"},
                            "condition": {"type": "string"},
                            "distinguishing_features": {
                                "type": "array",
                                "items": {"type": "string"},
                            },
                        },
                    },
                    "context": {
                        "type": "string",
                        "description": "Environmental context",
                    },
                    "visible_text": {
                        "type": "string",
                        "description": "Any visible text",
                    },
                    "brand": {
                        "type": "string",
                        "description": "Identified brand if any",
                    },
                },
                "required": ["main_object", "confidence", "attributes", "context"],
            },
        }

        # Generate response
        response = model.generate_content(
            [
                prompt,
                image_part,
                f"Analyze this image and respond with a JSON object matching this schema: {json.dumps(response_format)}",
            ],
            generation_config={"temperature": 0.2, "top_p": 0.8, "top_k": 40},
        )

        # Parse the response to ensure it's valid JSON
        try:
            result = json.loads(response.text)
        except json.JSONDecodeError:
            # If response isn't valid JSON, create a structured response
            result = {
                "main_object": "unknown",
                "confidence": 0.0,
                "attributes": {
                    "color": "unknown",
                    "size": "unknown",
                    "condition": "unknown",
                    "distinguishing_features": [],
                },
                "context": "Could not analyze image",
                "visible_text": "",
                "brand": "",
            }

        processing_time = time.time() - start_time

        return {
            "analysis": result,
            "metadata": {
                "model": "gemini-pro-vision",
                "processing_time": processing_time,
                "timestamp": time.time(),
            },
        }

    except Exception as e:
        logger.error(f"Error in Gemini processing: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/analyze")
async def analyze_image(file: UploadFile):
    """
    Endpoint to analyze an image using Gemini Vision.
    """
    start_time = time.time()

    try:
        # Generate unique filename
        file_id = str(uuid.uuid4())
        blob_path = f"uploads/{file_id}.jpg"

        # Upload timing
        upload_start = time.time()

        # Upload to GCS
        bucket = storage_client.bucket(BUCKET_NAME)
        blob = bucket.blob(blob_path)
        content = await file.read()
        blob.upload_from_string(content, content_type=file.content_type)

        upload_time = time.time() - upload_start

        # Get Gemini analysis
        analysis_start = time.time()
        analysis_result = get_gemini_response(blob_path)
        analysis_time = time.time() - analysis_start

        total_time = time.time() - start_time

        response = {
            "status": "success",
            "data": analysis_result["analysis"],
            "metadata": {
                **analysis_result["metadata"],
                "file_id": file_id,
                "storage_path": blob_path,
                "upload_time": upload_time,
                "analysis_time": analysis_time,
                "total_processing_time": total_time,
            },
        }

        logger.info(f"Successfully processed image {file_id}")
        return JSONResponse(content=response)

    except Exception as e:
        logger.error(f"Error processing image: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy"}
