import { genkit } from "genkit";
import { googleAI, gemini20FlashExp } from "@genkit-ai/googleai";
import { readFileSync } from "fs";
import { FootwearStatus } from "../utils/genkit-setup";

// Initialize Genkit
const ai = genkit({
  plugins: [googleAI()],
  model: gemini20FlashExp,
});

async function testFootMeasurementValidation() {
  try {
    // Test cases with different images
    const testCases = [
      {
        image: "test-images/feet-with-shoes.jpg",
        expectedStatus: FootwearStatus.WEARING_FOOTWEAR,
      },
      {
        image: "test-images/feet-with-socks.jpg",
        expectedStatus: FootwearStatus.WEARING_SOCKS,
      },
      {
        image: "test-images/bare-feet.jpg",
        expectedStatus: FootwearStatus.BARE_FEET,
      },
    ];

    for (const testCase of testCases) {
      console.log(`Testing image: ${testCase.image}`);

      // Read test image
      const imageBase64 = readFileSync(testCase.image, "base64");

      const systemPrompt = `You are a foot measurement validation expert. Your task is to analyze images of feet and determine if they are suitable for measurement.
                    You must categorize what you see into exactly one of these three categories:
                    1. WEARING_FOOTWEAR - if you see any shoes, sandals, slippers, or any other footwear
                    2. WEARING_SOCKS - if you see socks or stockings but no footwear
                    3. BARE_FEET - if you see bare feet with no socks or footwear
                    
                    Be very precise in your analysis. The measurement will only be accurate with bare feet.
                    If you're unsure or can't clearly see the feet, err on the side of caution and respond with WEARING_FOOTWEAR.
                    Respond ONLY with one of these three exact strings, nothing else.`;

      // Test the model response
      const { text } = await ai.generate([
        { text: systemPrompt },
        { media: { url: `data:image/jpeg;base64,${imageBase64}` } },
        { text: "What is the foot status in this image?" },
      ]);

      const result = text.trim().toUpperCase();
      console.log("Result:", result);
      console.log("Expected:", testCase.expectedStatus);
      console.log("Test passed:", result === testCase.expectedStatus);
      console.log("---");
    }
  } catch (error) {
    console.error("Test failed:", error);
  }
}

// Run test
testFootMeasurementValidation();
