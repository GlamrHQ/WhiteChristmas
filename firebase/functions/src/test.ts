import { genkit } from "genkit";
import { googleAI, gemini20FlashExp } from "@genkit-ai/googleai";
import * as admin from "firebase-admin";
import { readFileSync } from "fs";
import { z } from "zod";

// Initialize Firebase Admin with service account for testing
const serviceAccount = require("../service-account.json");
admin.initializeApp({
  credential: admin.credential.cert(serviceAccount),
});

// Initialize Genkit
const ai = genkit({
  plugins: [googleAI()],
  model: gemini20FlashExp,
});

// Define and register the tool
const getShoesList = ai.defineTool(
  {
    name: "getShoesList",
    description:
      "Gets the list of all shoe names and their document IDs from the database",
    inputSchema: z.object({
      dummy: z
        .boolean()
        .optional()
        .describe("Dummy parameter to satisfy API requirements"),
    }),
    outputSchema: z.array(
      z.object({
        name: z.string(),
        documentId: z.string(),
      })
    ),
  },
  async () => {
    const snapshot = await admin.firestore().collection("shoes").get();
    return snapshot.docs.map((doc) => ({
      name: doc.data().name,
      documentId: doc.id,
    }));
  }
);

async function testDetectShoe() {
  try {
    // First, let's add some test data to Firestore
    const testShoes = [
      { name: "Nike Air Max 90" },
      { name: "Adidas Ultraboost" },
      { name: "Jordan 1 Retro High" },
    ];

    // Keep track of created document references
    const testDocRefs: admin.firestore.DocumentReference[] = [];

    const batch = admin.firestore().batch();
    testShoes.forEach((shoe) => {
      const ref = admin.firestore().collection("shoes").doc();
      testDocRefs.push(ref);
      batch.set(ref, shoe);
    });
    await batch.commit();

    // Read test image
    const imageBase64 = readFileSync("test-images/test-shoe.jpg", "base64");

    // Test the model response
    const { text } = await ai.generate({
      prompt: [
        {
          text: `You are a shoe detection expert. Your task is to identify shoes in images and match them against a known database of shoes.
              If you detect a shoe but it's not in the database, respond with "UNKNOWN_SHOE".
              If you don't detect any shoe in the image, respond with "SHOE_NOT_FOUND".
              Use the getShoesList tool to fetch the list of known shoes and only return a shoe name if it exactly matches one from the list.
              Be very precise in your matching - the shoe must be an exact match to return its name.`,
        },
        { media: { url: `data:image/jpeg;base64,${imageBase64}` } },
        {
          text: "What is the name of this shoe? Please use the getShoesList tool to verify against our database.",
        },
      ],
      tools: [getShoesList],
    });

    console.log("Test Result:", text);

    // Clean up only the test data
    const cleanupBatch = admin.firestore().batch();
    testDocRefs.forEach((ref) => {
      cleanupBatch.delete(ref);
    });
    await cleanupBatch.commit();

    console.log("Cleaned up test data successfully");
  } catch (error) {
    console.error("Test failed:", error);
  }
}

// Run test
testDetectShoe();
