# Firestore Collection Analysis Script

This directory contains utility scripts for analyzing your Firestore database.

## List Collections Script

The `list-collections.ts` script analyzes your Firestore database and outputs all collections along with their schema (field names and types).

### Prerequisites

1. Make sure you have a `service-account.json` file in the parent directory (`firebase/`). You can get this from the Firebase Console:
   - Go to Project Settings > Service Accounts
   - Click "Generate New Private Key"
   - Save the file as `service-account.json` in the `firebase` directory

2. Install dependencies:
```bash
npm install firebase-admin typescript ts-node
```

### Usage

Run the script using node:
```bash
NODE_OPTIONS='--loader ts-node/esm' node --experimental-specifier-resolution=node list-collections.ts
```

### Output

The script will output:
- All collections in your Firestore database
- For each collection:
  - Collection name
  - Field names and their types (based on sampling up to 100 documents)

### Note

This script is for local debugging purposes only. Do not commit your `service-account.json` to version control. 