#!/bin/bash
set -e

# Get the absolute path to the firebase directory
FIREBASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Function to run tests for a specific function
run_tests() {
    local function_name=$1
    echo "Running tests for $function_name..."
    
    # Check if conda is available
    if ! command -v conda &> /dev/null; then
        echo "Error: Conda is required but not found. Please install Miniconda or Anaconda."
        exit 1
    fi
    
    # Create or update conda environment
    local env_name="firebase_${function_name}_test"
    
    # Remove environment if it exists
    conda env remove -n "$env_name" -y &> /dev/null || true
    
    # Create new environment with Python 3.10
    echo "Creating Conda environment with Python 3.10..."
    conda create -n "$env_name" python=3.10 -y
    
    # Activate conda environment
    echo "Activating Conda environment..."
    source "$(conda info --base)/etc/profile.d/conda.sh"
    conda activate "$env_name"
    
    # Create and activate virtual environment
    echo "Creating virtual environment..."
    cd "${FIREBASE_DIR}/functions/${function_name}"
    python -m venv venv
    source venv/bin/activate
    
    # Install dependencies
    echo "Installing dependencies..."
    pip install --no-cache-dir -r requirements.txt
    
    # Run tests
    echo "Running tests..."
    python -m pytest tests/ -v
    
    # Cleanup
    deactivate
    conda deactivate
    
    echo "Tests completed for $function_name"
}

# Run tests for specific function or all functions
if [ -z "$1" ]; then
    # Run tests for all functions
    for func_dir in "${FIREBASE_DIR}/functions/"*/; do
        func_name=$(basename "$func_dir")
        if [ "$func_name" != "shared" ] && [ -d "${func_dir}/tests" ]; then
            run_tests "$func_name"
        fi
    done
else
    # Run tests for specific function
    if [ -d "${FIREBASE_DIR}/functions/$1/tests" ]; then
        run_tests "$1"
    else
        echo "Error: No tests found for function $1"
        exit 1
    fi
fi 