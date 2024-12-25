from fastapi import FastAPI, UploadFile, HTTPException
from fastapi.responses import JSONResponse
from google.cloud import storage
from google import genai
from google.genai import types
import time
import logging
import uuid
import os
from typing import Dict, BinaryIO
import json
import magic
from io import BytesIO
import base64

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="Object Detection API")

# Initialize GCP clients
storage_client = storage.Client()
BUCKET_NAME = os.getenv("BUCKET_NAME", "object-detection-images")

# Initialize Vertex AI client
PROJECT_ID = os.getenv("PROJECT_ID")
LOCATION = os.getenv("LOCATION", "asia-south1")
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.0-flash-exp")


def get_gemini_response(image_content: bytes, mime_type: str) -> Dict:
    """
    Get Gemini 2.0 Flash's analysis of the image using google-genai.
    The image is passed directly as bytes to avoid re-downloading from GCS.
    """
    start_time = time.time()

    try:
        # Initialize Gemini client
        client = genai.Client(vertexai=True, project=PROJECT_ID, location=LOCATION)

        # Convert image to base64 and create image part
        image_part = types.Part.from_bytes(data=image_content, mime_type=mime_type)

        # Create text prompt part
        text_part = types.Part.from_text(
            """You are an expert computer vision system analyzing images.
        Your task is to provide detailed information about objects in the image.
        Focus on:
        - Main object identification
        - Visual characteristics (color, shape, size)
        - State/condition
        - Context/environment
        - Any text visible
        - Brand identification if applicable
        
        Format your response as a JSON object with the following structure:
        {
            "main_object": "string",
            "confidence": number,
            "attributes": {
                "color": "string",
                "size": "string",
                "condition": "string",
                "distinguishing_features": ["string"]
            },
            "context": "string",
            "visible_text": "string",
            "brand": "string"
        }
        
        Be precise and confident in your observations.
        Return only the JSON response without any additional text."""
        )

        # Set up model configuration
        model = GEMINI_MODEL
        contents = [types.Content(role="user", parts=[image_part, text_part])]

        # Configure tools and safety settings
        tools = [types.Tool(google_search=types.GoogleSearch())]

        generate_content_config = types.GenerateContentConfig(
            temperature=0.2,
            top_p=0.95,
            max_output_tokens=8192,
            response_modalities=["TEXT"],
            safety_settings=[
                types.SafetySetting(
                    category="HARM_CATEGORY_HATE_SPEECH", threshold="OFF"
                ),
                types.SafetySetting(
                    category="HARM_CATEGORY_DANGEROUS_CONTENT", threshold="OFF"
                ),
                types.SafetySetting(
                    category="HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold="OFF"
                ),
                types.SafetySetting(
                    category="HARM_CATEGORY_HARASSMENT", threshold="OFF"
                ),
            ],
            tools=tools,
        )

        # Generate response
        response_text = ""
        for chunk in client.models.generate_content_stream(
            model=model,
            contents=contents,
            config=generate_content_config,
        ):
            if chunk.candidates and chunk.candidates[0].content.parts:
                response_text += str(chunk.candidates[0].content.parts[0])

        # Parse JSON response
        try:
            result = json.loads(response_text)
        except json.JSONDecodeError as e:
            logger.warning(f"Failed to parse Gemini response as JSON: {e}")
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
                "model": model,
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
    Endpoint to analyze an image using Gemini 2.0 Flash.
    """
    start_time = time.time()

    try:
        # Generate unique filename
        file_id = str(uuid.uuid4())

        # Read file content into memory
        content = await file.read()

        # Determine MIME type
        mime_type = file.content_type
        if not mime_type:
            mime_type = magic.from_buffer(content, mime=True)

        # Determine file extension from content type or filename
        if file.content_type:
            file_extension = "." + file.content_type.split("/")[1]
        else:
            file_extension = os.path.splitext(file.filename)[1] or ".jpg"
        blob_path = f"uploads/{file_id}{file_extension}"

        # Upload timing
        upload_start = time.time()

        # Upload to GCS
        bucket = storage_client.bucket(BUCKET_NAME)
        blob = bucket.blob(blob_path)
        blob.upload_from_string(content, content_type=mime_type)

        upload_time = time.time() - upload_start

        # Get Gemini analysis (passing image content directly)
        analysis_start = time.time()
        analysis_result = get_gemini_response(content, mime_type)
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
