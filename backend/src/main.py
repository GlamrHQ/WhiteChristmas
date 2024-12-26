import time
import logging
import uuid
import os
from typing import Dict, List
import json
from fastapi import FastAPI, UploadFile, HTTPException, Form, File
from fastapi.responses import JSONResponse
from google.cloud import storage
from google import genai
from google.genai import types
import magic
import numpy as np
from PIL import Image
import io

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="Object Detection API")

# Initialize GCP clients
storage_client = storage.Client()
BUCKET_NAME = os.getenv("BUCKET_NAME", "object-detection-images")
MAX_IMAGES_IN_GRID = int(os.getenv("MAX_IMAGES_IN_GRID", "4"))

# Initialize Vertex AI client
PROJECT_ID = os.getenv("PROJECT_ID")
LOCATION = os.getenv("LOCATION", "asia-south1")
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.0-flash-exp")


def create_image_grid(images: List[Image.Image], max_images: int = 4) -> Image.Image:
    """
    Create a square grid from a list of images.

    Args:
        images: List of PIL Image objects
        max_images: Maximum number of images in the grid (default: 4)

    Returns:
        PIL Image object containing the grid
    """
    # Limit number of images
    images = images[:max_images]
    n = len(images)

    if n == 0:
        raise ValueError("No images provided")

    # Calculate grid dimensions
    grid_size = int(np.ceil(np.sqrt(n)))

    # Resize images to same size
    max_size = 512  # Maximum size for each image
    resized_images = []
    for img in images:
        # Calculate new size maintaining aspect ratio
        ratio = min(max_size / img.size[0], max_size / img.size[1])
        new_size = tuple(int(dim * ratio) for dim in img.size)
        resized_images.append(img.resize(new_size, Image.Resampling.LANCZOS))

    # Find maximum dimensions
    max_width = max(img.size[0] for img in resized_images)
    max_height = max(img.size[1] for img in resized_images)

    # Create blank grid with border
    border_size = 10  # Border size in pixels
    grid_width = grid_size * (max_width + border_size) + border_size
    grid_height = grid_size * (max_height + border_size) + border_size
    grid_img = Image.new("RGB", (grid_width, grid_height), color="white")

    # Place images in grid
    for idx, img in enumerate(resized_images):
        row = idx // grid_size
        col = idx % grid_size
        x = col * (max_width + border_size) + border_size
        y = row * (max_height + border_size) + border_size

        # Center image in its cell
        x_center = x + (max_width - img.size[0]) // 2
        y_center = y + (max_height - img.size[1]) // 2
        grid_img.paste(img, (x_center, y_center))

    return grid_img


def process_llm_response(response_text: str) -> str:
    """
    Process and clean the LLM response text by removing markdown formatting and extracting JSON content.

    Args:
        response_text: Raw response text from the LLM

    Returns:
        Cleaned JSON string ready for parsing
    """
    # Remove markdown code block syntax if present
    if response_text.startswith("```") and response_text.endswith("```"):
        # Remove the first line (```json) and last line (```)
        response_text = "\n".join(response_text.split("\n")[1:-1])

    # If the response is in the text field, extract it
    if "text='" in response_text and response_text.endswith("'"):
        response_text = response_text.split("text='")[-1][:-1]
        # Remove markdown code block syntax again if present
        if response_text.startswith("```") and response_text.endswith("```"):
            response_text = "\n".join(response_text.split("\n")[1:-1])

    return response_text


def get_gemini_response(
    image_content: bytes,
    mime_type: str,
    enable_google_search: bool = True,
    is_grid: bool = False,
    grid_size: int = 1,
) -> Dict:
    """
    Get Gemini 2.0 Flash's analysis of the image using google-genai.
    The image is passed directly as bytes to avoid re-downloading from GCS.

    Args:
        image_content: Raw image bytes
        mime_type: MIME type of the image
        enable_google_search: Whether to enable Google Search tool (default: True)
        is_grid: Whether the image is a grid of multiple images (default: False)
        grid_size: Number of images in the grid (default: 1)
    """
    start_time = time.time()

    try:
        # Initialize Gemini client
        client = genai.Client(vertexai=True, project=PROJECT_ID, location=LOCATION)

        # Convert image to base64 and create image part
        image_part = types.Part.from_bytes(data=image_content, mime_type=mime_type)

        # Create text prompt part based on whether it's a grid or single image
        if is_grid:
            text_part = types.Part.from_text(
                f"""You are an expert computer vision system analyzing a grid of {grid_size} images.
            Your task is to analyze EACH image in the grid separately, maintaining STRICT ORDER:
            - Start from top-left (index 0)
            - Move right in each row
            - When row ends, move to leftmost image of next row
            - Continue until all {grid_size} images are analyzed
            
            For each image, provide detailed information about:
            - Main object identification
            - Visual characteristics (color, shape, size)
            - State/condition
            - Context/environment
            - Any text visible
            - Brand identification if applicable
            
            CRITICAL: The order of objects in your JSON array MUST match the order of images in the grid:
            - Index 0: Top-left image
            - Index 1: Second image in top row
            - And so on, row by row from left to right
            
            Format your response as a JSON array with exactly {grid_size} objects in this order:
            [
                {{  // Index 0: Top-left image
                    "position": "top-left, index 0",
                    "main_object": "string",
                    "confidence": number,
                    "attributes": {{
                        "color": "string",
                        "size": "string",
                        "condition": "string",
                        "distinguishing_features": ["string"]
                    }},
                    "context": "string",
                    "visible_text": "string",
                    "brand": "string"
                }},
                {{  // Index 1: Next image in top row
                    "position": "top row, index 1",
                    ...
                }},
                // Continue for all images in order
            ]
            
            Be precise and confident in your observations.
            Return only the JSON array response without any additional text.
            Make sure to analyze each image independently and maintain the exact order as specified."""
            )
        else:
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
        tools = (
            [types.Tool(google_search=types.GoogleSearch())]
            if enable_google_search
            else []
        )

        generate_content_config = types.GenerateContentConfig(
            temperature=0,
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
            response_mime_type="application/json",
        )

        # Generate response
        response_text = ""
        for chunk in client.models.generate_content_stream(
            model=model,
            contents=contents,
            config=generate_content_config,
        ):
            if chunk.candidates and chunk.candidates[0].content.parts:
                response_text += chunk.candidates[0].content.parts[0].text

        # Log the response text
        logger.info("Gemini response text: %s", response_text)

        # Parse JSON response
        try:
            result = json.loads(response_text)
            if is_grid and not isinstance(result, list):
                logger.warning("Expected array response for grid but got object")
                result = [result] * grid_size  # Fallback: duplicate the result
        except json.JSONDecodeError as e:
            logger.warning("Failed to parse Gemini response as JSON: %s", e)
            logger.warning("Response causing error: %s", response_text)
            if is_grid:
                result = [
                    {
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
                ] * grid_size
            else:
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
        logger.error("Error in Gemini processing: %s", str(e))
        raise HTTPException(status_code=500, detail=str(e)) from e


@app.post("/analyze")
async def analyze_image(
    file: UploadFile,
    enable_google_search: bool = Form(
        default=True, description="Enable Google Search tool for enhanced analysis"
    ),
):
    """
    Endpoint to analyze an image using Gemini 2.0 Flash.

    Args:
        file: The image file to analyze
        enable_google_search: Whether to enable Google Search tool for enhanced analysis (default: True)
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
        analysis_result = get_gemini_response(content, mime_type, enable_google_search)
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
                "google_search_enabled": enable_google_search,
            },
        }

        logger.info("Successfully processed image %s", file_id)
        return JSONResponse(content=response)

    except Exception as e:
        logger.error("Error processing image: %s", str(e))
        raise HTTPException(status_code=500, detail=str(e)) from e


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "healthy"}


@app.post("/analyze-batch")
async def analyze_batch_images(
    files: List[UploadFile] = File(...),
    enable_google_search: bool = Form(
        default=True, description="Enable Google Search tool for enhanced analysis"
    ),
):
    """
    Endpoint to analyze multiple images in batches using Gemini 2.0 Flash.
    Images are arranged in a grid for efficient processing.

    Args:
        files: List of image files to analyze
        enable_google_search: Whether to enable Google Search tool for enhanced analysis (default: True)
    """
    start_time = time.time()
    all_results = []

    try:
        # Process images in batches based on MAX_IMAGES_IN_GRID
        for i in range(0, len(files), MAX_IMAGES_IN_GRID):
            batch = files[i : i + MAX_IMAGES_IN_GRID]
            batch_start_time = time.time()

            # Read all images in batch
            pil_images = []
            file_ids = []
            upload_times = []

            for file in batch:
                file_id = str(uuid.uuid4())
                file_ids.append(file_id)

                # Read file content
                content = await file.read()

                # Convert to PIL Image
                img = Image.open(io.BytesIO(content))
                pil_images.append(img)

                # Upload to GCS
                upload_start = time.time()
                file_extension = os.path.splitext(file.filename)[1] or ".jpg"
                blob_path = f"uploads/{file_id}{file_extension}"

                bucket = storage_client.bucket(BUCKET_NAME)
                blob = bucket.blob(blob_path)
                blob.upload_from_string(content, content_type=file.content_type)

                upload_times.append(time.time() - upload_start)

            # Create grid image
            grid_image = create_image_grid(pil_images, MAX_IMAGES_IN_GRID)

            # Convert grid to bytes
            grid_buffer = io.BytesIO()
            grid_image.save(grid_buffer, format="JPEG")
            grid_bytes = grid_buffer.getvalue()

            # Get Gemini analysis for the grid
            analysis_start = time.time()
            analysis_result = get_gemini_response(
                grid_bytes,
                "image/jpeg",
                enable_google_search,
                is_grid=True,
                grid_size=MAX_IMAGES_IN_GRID,
            )
            analysis_time = time.time() - analysis_start

            # Process batch results
            batch_time = time.time() - batch_start_time

            for idx, file_id in enumerate(file_ids):
                result = {
                    "status": "success",
                    "data": analysis_result["analysis"],
                    "metadata": {
                        **analysis_result["metadata"],
                        "file_id": file_id,
                        "storage_path": f"uploads/{file_id}{os.path.splitext(batch[idx].filename)[1] or '.jpg'}",
                        "upload_time": upload_times[idx],
                        "analysis_time": analysis_time
                        / len(batch),  # Divide by batch size
                        "total_processing_time": batch_time
                        / len(batch),  # Divide by batch size
                        "google_search_enabled": enable_google_search,
                        "batch_info": {
                            "batch_size": len(batch),
                            "batch_index": i // MAX_IMAGES_IN_GRID,
                            "position_in_batch": idx,
                        },
                    },
                }
                all_results.append(result)

        total_time = time.time() - start_time

        response = {
            "status": "success",
            "results": all_results,
            "metadata": {
                "total_images": len(files),
                "total_batches": (len(files) + MAX_IMAGES_IN_GRID - 1)
                // MAX_IMAGES_IN_GRID,
                "max_images_per_batch": MAX_IMAGES_IN_GRID,
                "total_processing_time": total_time,
            },
        }

        logger.info("Successfully processed %d images in batches", len(files))
        return JSONResponse(content=response)

    except Exception as e:
        logger.error("Error processing batch images: %s", str(e))
        raise HTTPException(status_code=500, detail=str(e)) from e
