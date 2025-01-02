import admin from "firebase-admin";
import { fileURLToPath } from "url";
import { dirname, join } from "path";
import { createRequire } from "module";

const require = createRequire(import.meta.url);
const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// Initialize Firebase Admin with explicit path resolution
const serviceAccount = require(join(__dirname, "../service-account.json"));

admin.initializeApp({
  credential: admin.credential.cert(serviceAccount),
});

const db = admin.firestore();

async function analyzeCollection(
  collectionRef: admin.firestore.CollectionReference
) {
  const schema: { [key: string]: Set<string> } = {};
  const snapshot = await collectionRef.limit(100).get();

  snapshot.forEach((doc) => {
    const data = doc.data();
    Object.entries(data).forEach(([key, value]) => {
      if (!schema[key]) {
        schema[key] = new Set();
      }
      schema[key].add(typeof value);
      // Add more specific type information for objects and arrays
      if (typeof value === "object" && value !== null) {
        if (Array.isArray(value)) {
          schema[key].add("array");
        } else if (value instanceof admin.firestore.Timestamp) {
          schema[key].add("timestamp");
        }
      }
    });
  });

  return Object.fromEntries(
    Object.entries(schema).map(([key, types]) => [key, Array.from(types)])
  );
}

async function listCollectionsAndSchema() {
  try {
    console.log("Analyzing Firestore collections and schema...\n");

    const collections = await db.listCollections();
    let collectionCount = 0;

    for (const collection of collections) {
      collectionCount++;
      console.log(`Collection: ${collection.id}`);
      console.log("Schema:");

      const schema = await analyzeCollection(collection);
      Object.entries(schema).forEach(([field, types]) => {
        console.log(`  ${field}: ${types.join(" | ")}`);
      });
      console.log("\n");
    }

    console.log(`Total collections found: ${collectionCount}`);
    process.exit(0);
  } catch (error) {
    console.error("Error:", error);
    process.exit(1);
  }
}

listCollectionsAndSchema();
