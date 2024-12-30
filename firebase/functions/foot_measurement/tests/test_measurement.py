import pytest
import numpy as np
import cv2
from main import (
    get_perspective_transform,
    segment_foot,
    measure_foot,
    process_single_image
)

@pytest.fixture
def sample_image():
    """Create a sample test image."""
    img = np.zeros((800, 600, 3), dtype=np.uint8)
    cv2.rectangle(img, (200, 300), (400, 600), (255, 255, 255), -1)
    return img

@pytest.fixture
def sample_markers():
    """Create sample ArUco marker corners."""
    return np.array([
        [[[100, 100], [100, 150], [150, 150], [150, 100]]],
        [[[400, 100], [400, 150], [450, 150], [450, 100]]]
    ])

def test_perspective_transform(sample_markers):
    transform_matrix, pixels_per_cm = get_perspective_transform(sample_markers)
    assert transform_matrix.shape == (3, 3)
    assert pixels_per_cm > 0

def test_segment_foot(sample_image):
    mask = segment_foot(sample_image)
    assert mask.shape[:2] == sample_image.shape[:2]
    assert mask.dtype == np.uint8

def test_measure_foot():
    # Create a simple mask with known dimensions
    mask = np.zeros((1000, 1000), dtype=np.uint8)
    cv2.rectangle(mask, (300, 400), (700, 800), 255, -1)
    
    pixels_per_cm = 10  # 10 pixels = 1 cm
    measurements = measure_foot(mask, pixels_per_cm)
    
    assert "length" in measurements
    assert "width" in measurements
    assert measurements["length"] == 40.0  # 400 pixels / 10 pixels per cm
    assert measurements["width"] == 20.0   # 200 pixels / 10 pixels per cm
