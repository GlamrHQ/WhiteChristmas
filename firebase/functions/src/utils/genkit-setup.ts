import { genkit } from "genkit";
import { googleAI, gemini20FlashExp } from "@genkit-ai/googleai";
import { defineSecret } from "firebase-functions/params";

// Define secret for Google AI API key
export const googleAIapiKey = defineSecret("GOOGLE_GENAI_API_KEY");

// Initialize Genkit
export const ai = genkit({
  plugins: [googleAI()],
  model: gemini20FlashExp,
});

// Common response types for foot measurement
export enum FootwearStatus {
  WEARING_FOOTWEAR = "WEARING_FOOTWEAR",
  WEARING_SOCKS = "WEARING_SOCKS",
  BARE_FEET = "BARE_FEET",
}
