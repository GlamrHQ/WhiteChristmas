response_schema = {
    "type": "object",
    "properties": {
        "main_object": {"type": "string"},
        "confidence": {"type": "number"},
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
            "required": [
                "color",
                "size",
                "condition",
                "distinguishing_features",
            ],
        },
        "context": {"type": "string"},
        "visible_text": {"type": "string"},
        "brand": {"type": "string"},
    },
    "required": [
        "main_object",
        "confidence",
        "attributes",
        "context",
        "visible_text",
        "brand",
    ],
}
