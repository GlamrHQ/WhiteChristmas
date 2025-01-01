import * as admin from "firebase-admin";

// Initialize Firebase Admin if not already initialized
if (!admin.apps.length) {
  admin.initializeApp();
}

export const db = admin.firestore();

export async function getShoeNames(
  _input: { dummy?: boolean } = {}
): Promise<string[]> {
  const snapshot = await db.collection("shoes").get();
  return snapshot.docs.map((doc) => doc.data().name);
}
