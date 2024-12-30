#!/bin/bash
set -e

# Get the absolute path to the firebase directory
FIREBASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Function to deploy a specific function
deploy_function() {
    local function_name=$1
    echo "Deploying $function_name..."
    
    # Check if conda is available
    if ! command -v conda &> /dev/null; then
        echo "Error: Conda is required but not found. Please install Miniconda or Anaconda."
        exit 1
    fi
    
    # Create or update conda environment
    local env_name="firebase_${function_name}"
    
    # Remove environment if it exists
    conda env remove -n "$env_name" -y &> /dev/null || true
    
    # Create new environment with Python 3.10
    echo "Creating Conda environment with Python 3.10..."
    conda create -n "$env_name" python=3.10 -y
    
    # Activate conda environment
    echo "Activating Conda environment..."
    source "$(conda info --base)/etc/profile.d/conda.sh"
    conda activate "$env_name"
    
    # Create the venv directory that Firebase expects
    echo "Creating Firebase-compatible virtual environment..."
    cd "${FIREBASE_DIR}/functions/${function_name}"
    rm -rf venv
    python -m venv venv
    source venv/bin/activate
    
    # Install dependencies from requirements.txt
    echo "Installing dependencies..."
    pip install --no-cache-dir -r requirements.txt
    
    # Clean pip cache to reduce size
    pip cache purge
    
    # Deploy function
    echo "Deploying to Firebase..."
    cd "${FIREBASE_DIR}"
    firebase deploy --only functions:$function_name
    
    # Cleanup
    deactivate
    conda deactivate
}

# Deploy specific function or all functions
if [ -z "$1" ]; then
    # Deploy all functions
    for func_dir in "${FIREBASE_DIR}/functions/"*/; do
        func_name=$(basename "$func_dir")
        if [ "$func_name" != "shared" ]; then
            deploy_function "$func_name"
        fi
    done
else
    # Deploy specific function
    deploy_function "$1"
fi