import numpy as np
import cv2
from firebase_functions import https_fn
from firebase_admin import initialize_app
import functions_framework
import json
import base64
from typing import List, Tuple, Dict, Optional

class ImageProcessor:
    @staticmethod
    def decode_base64(image_base64: str) -> np.ndarray:
        """Decode base64 image to numpy array."""
        img_data = base64.b64decode(image_base64)
        nparr = np.frombuffer(img_data, np.uint8)
        return cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    
    @staticmethod
    def detect_markers(image: np.ndarray) -> Tuple[np.ndarray, Optional[List[int]]]:
        """Detect ArUco markers in the image."""
        aruco_dict = cv2.aruco.Dictionary_get(cv2.aruco.DICT_4X4_50)
        parameters = cv2.aruco.DetectorParameters_create()
        corners, ids, _ = cv2.aruco.detectMarkers(image, aruco_dict, parameters=parameters)
        
        if ids is None:
            return np.array([]), None
        
        return corners, ids.flatten().tolist()

# Initialize Firebase app
initialize_app()

@https_fn.on_request()
def health(req: https_fn.Request) -> https_fn.Response:
    """Health check endpoint to verify the API is up and running."""
    # Add CORS headers
    if req.method == 'OPTIONS':
        headers = {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET',
            'Access-Control-Allow-Headers': 'Content-Type',
            'Access-Control-Max-Age': '3600'
        }
        return https_fn.Response('', status=204, headers=headers)

    headers = {'Access-Control-Allow-Origin': '*'}
    
    try:
        return https_fn.Response(
            json.dumps({
                "status": "healthy",
                "timestamp": str(np.datetime64('now')),
                "service": "foot_measurement"
            }),
            status=200,
            headers=headers
        )
    except Exception as e:
        return https_fn.Response(
            json.dumps({
                "status": "unhealthy",
                "error": str(e)
            }),
            status=500,
            headers=headers
        )

def get_perspective_transform(marker_corners: np.ndarray, known_width_cm: float = 30.0) -> Tuple[np.ndarray, float]:
    """
    Calculate perspective transform matrix and scale factor from marker positions.
    """
    marker1_center = marker_corners[0][0].mean(axis=0)
    marker2_center = marker_corners[1][0].mean(axis=0)
    
    orientation = marker2_center - marker1_center
    angle = np.arctan2(orientation[1], orientation[0])
    
    ideal_width_pixels = 1000
    pixels_per_cm = ideal_width_pixels / known_width_cm
    
    src_points = np.float32([
        marker_corners[0][0][0],
        marker_corners[0][0][2],
        marker_corners[1][0][0],
        marker_corners[1][0][2]
    ])
    
    margin = ideal_width_pixels * 0.1
    dst_points = np.float32([
        [margin, margin],
        [margin, margin + pixels_per_cm * 5],
        [margin + ideal_width_pixels, margin],
        [margin + ideal_width_pixels, margin + pixels_per_cm * 5]
    ])
    
    transform_matrix = cv2.getPerspectiveTransform(src_points, dst_points)
    
    return transform_matrix, pixels_per_cm

def segment_foot(image: np.ndarray) -> np.ndarray:
    """Segment the foot from the background."""
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    blurred = cv2.GaussianBlur(gray, (7, 7), 0)
    _, thresh = cv2.threshold(blurred, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
    
    kernel = np.ones((5,5), np.uint8)
    mask = cv2.morphologyEx(thresh, cv2.MORPH_CLOSE, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    
    return mask

def measure_foot(mask: np.ndarray, pixels_per_cm: float) -> Dict[str, float]:
    """Measure foot dimensions from the segmented mask."""
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        raise ValueError("No foot contour found")
    
    foot_contour = max(contours, key=cv2.contourArea)
    rect = cv2.minAreaRect(foot_contour)
    box = cv2.boxPoints(rect)
    box = np.int0(box)
    
    width = rect[1][0]
    height = rect[1][1]
    
    length_cm = max(width, height) / pixels_per_cm
    width_cm = min(width, height) / pixels_per_cm
    
    return {
        "length": round(length_cm, 1),
        "width": round(width_cm, 1)
    }

def process_single_image(
    image: np.ndarray,
    transform_matrix: np.ndarray,
    pixels_per_cm: float
) -> Dict[str, float]:
    """Process a single image to get foot measurements."""
    height, width = image.shape[:2]
    warped = cv2.warpPerspective(image, transform_matrix, (width * 2, height * 2))
    foot_mask = segment_foot(warped)
    return measure_foot(foot_mask, pixels_per_cm)

@https_fn.on_call()
def measure_feet(req: https_fn.Request) -> https_fn.Response:
    """Cloud function to process multiple foot images and return measurements."""
    try:
        data = req.data
        if not data or 'images' not in data:
            return https_fn.Response(
                json.dumps({"error": "No images provided"}),
                status=400
            )
        
        all_measurements = []
        
        for image_base64 in data['images']:
            image = ImageProcessor.decode_base64(image_base64)
            marker_corners, marker_ids = ImageProcessor.detect_markers(image)
            
            if len(marker_corners) < 2:
                continue
            
            transform_matrix, pixels_per_cm = get_perspective_transform(marker_corners)
            measurements = process_single_image(image, transform_matrix, pixels_per_cm)
            all_measurements.append(measurements)
        
        if not all_measurements:
            return https_fn.Response(
                json.dumps({"error": "Could not process any images"}),
                status=400
            )
        
        avg_length = np.mean([m['length'] for m in all_measurements])
        avg_width = np.mean([m['width'] for m in all_measurements])
        length_std = np.std([m['length'] for m in all_measurements])
        width_std = np.std([m['width'] for m in all_measurements])
        confidence = 1.0 / (1.0 + length_std + width_std)
        
        result = {
            "foot_length_cm": round(avg_length, 1),
            "foot_width_cm": round(avg_width, 1),
            "confidence": round(confidence, 2),
            "num_images_processed": len(all_measurements)
        }
        
        return https_fn.Response(json.dumps(result), status=200)
        
    except Exception as e:
        return https_fn.Response(
            json.dumps({"error": str(e)}),
            status=500
        )