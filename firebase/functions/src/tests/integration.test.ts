import * as dotenv from "dotenv";
dotenv.config();

import * as firebase from "firebase/app";
import {
  getFunctions,
  httpsCallable,
  connectFunctionsEmulator,
} from "firebase/functions";
import { deleteApp } from "firebase/app";
import { getAuth, signInWithEmailAndPassword } from "firebase/auth";
import { readFileSync } from "fs";
import { join } from "path";
import { FootwearStatus } from "../utils/genkit-setup";

// Initialize Firebase App
const firebaseConfig = {
  projectId: process.env.TEST_FIREBASE_PROJECT_ID || "test-app-60df0",
  apiKey: process.env.TEST_FIREBASE_API_KEY,
};

// Debug log to check if env vars are loaded
console.log("Project ID:", process.env.TEST_FIREBASE_PROJECT_ID);
console.log("API Key exists:", !!process.env.TEST_FIREBASE_API_KEY);

// Initialize Firebase
const app = firebase.initializeApp(firebaseConfig);
const functions = getFunctions(app);
const auth = getAuth(app);

// If using emulator
if (process.env.USE_EMULATOR) {
  console.log("Using Firebase emulator");
  connectFunctionsEmulator(functions, "localhost", 5001);
}

// Helper function to get test image path
const getTestImagePath = (filename: string) =>
  join(__dirname, "../../test-images", filename);

async function setupAuth() {
  if (!process.env.TEST_EMAIL || !process.env.TEST_PASSWORD) {
    throw new Error(
      "TEST_EMAIL and TEST_PASSWORD environment variables are required"
    );
  }

  try {
    await signInWithEmailAndPassword(
      auth,
      process.env.TEST_EMAIL,
      process.env.TEST_PASSWORD
    );
    console.log("Authentication successful");
  } catch (error) {
    console.error("Authentication failed:", error);
    throw error;
  }
}

async function testShoeDetection() {
  try {
    console.log("\n=== Testing Shoe Detection ===");
    const detectShoe = httpsCallable(functions, "detectShoe");

    // Test with a known shoe
    const imageBase64 = readFileSync(
      getTestImagePath("test-shoe.jpg"),
      "base64"
    );
    const result = await detectShoe({ data: imageBase64 });

    console.log("Result:", result.data);

    // Type guard for shoe detection result
    interface ShoeDetectionResult {
      name: string;
      documentId: string;
    }

    function isValidShoeResult(data: any): data is ShoeDetectionResult {
      return (
        typeof data === "object" &&
        data !== null &&
        typeof data.name === "string" &&
        typeof data.documentId === "string"
      );
    }

    // Validate the result
    if (!isValidShoeResult(result.data)) {
      throw new Error("Invalid result format from shoe detection");
    }

    const isValidResult =
      (result.data.name === "UNKNOWN_SHOE" && result.data.documentId === "0") ||
      (result.data.name === "SHOE_NOT_FOUND" &&
        result.data.documentId === "-1") ||
      (typeof result.data.documentId === "string" &&
        result.data.documentId.length > 0);

    console.log("Test passed:", isValidResult);
  } catch (error) {
    console.error("Shoe detection test failed:", error);
  }
}

async function testFootMeasurement() {
  try {
    console.log("\n=== Testing Foot Measurement ===");
    const validateFootMeasurement = httpsCallable(
      functions,
      "validateFootMeasurement"
    );

    const testCases = [
      {
        image: getTestImagePath("feet-with-shoes.jpeg"),
        expectedStatus: FootwearStatus.WEARING_FOOTWEAR,
      },
      {
        image: getTestImagePath("feet-with-socks.jpg"),
        expectedStatus: FootwearStatus.WEARING_SOCKS,
      },
      {
        image: getTestImagePath("bare-feet.jpg"),
        expectedStatus: FootwearStatus.BARE_FEET,
      },
    ];

    for (const testCase of testCases) {
      console.log(`\nTesting image: ${testCase.image}`);
      const imageBase64 = readFileSync(testCase.image, "base64");
      const result = await validateFootMeasurement({ data: imageBase64 });

      console.log("Result:", result.data);
      console.log("Expected:", testCase.expectedStatus);
      console.log("Test passed:", result.data === testCase.expectedStatus);
    }
  } catch (error) {
    console.error("Foot measurement test failed:", error);
  }
}

async function runIntegrationTests() {
  try {
    await setupAuth();
    await testShoeDetection();
    await testFootMeasurement();
  } catch (error) {
    console.error("Integration tests failed:", error);
  } finally {
    // Clean up Firebase app
    await deleteApp(app);
  }
}

// Run tests
runIntegrationTests();
