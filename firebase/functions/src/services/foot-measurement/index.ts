import { onFlow } from "@genkit-ai/firebase/functions";
import { firebaseAuth } from "@genkit-ai/firebase/auth";
import { ai, googleAIapiKey, FootwearStatus } from "../../utils/genkit-setup";
import {
  FootMeasurementInputSchema,
  FootMeasurementOutputSchema,
} from "./types";

export const validateFootMeasurement = onFlow(
  ai,
  {
    name: "validateFootMeasurement",
    inputSchema: FootMeasurementInputSchema,
    outputSchema: FootMeasurementOutputSchema,
    authPolicy: firebaseAuth((user) => {
    //   if (!user.email_verified) {
    //     throw new Error("Verified email required to use this function");
    //   }
    }),
    httpsOptions: {
      secrets: [googleAIapiKey],
      cors: "*",
    },
  },
  async ({ data }) => {
    const systemPrompt = `You are a foot measurement validation expert. Your task is to analyze images of feet and determine if they are suitable for measurement.
                  You must categorize what you see into exactly one of these three categories:
                  1. WEARING_FOOTWEAR - if you see any shoes, sandals, slippers, or any other footwear
                  2. WEARING_SOCKS - if you see socks or stockings but no footwear
                  3. BARE_FEET - if you see bare feet with no socks or footwear
                  
                  Be very precise in your analysis. The measurement will only be accurate with bare feet.
                  If you're unsure or can't clearly see the feet, err on the side of caution and respond with WEARING_FOOTWEAR.
                  Respond ONLY with one of these three exact strings, nothing else.`;

    const { text } = await ai.generate([
      { text: systemPrompt },
      { media: { url: `data:image/jpeg;base64,${data}` } },
      { text: "What is the foot status in this image?" },
    ]);

    // Clean and validate the response
    const result = text.trim().toUpperCase();

    // Ensure the response is one of our enum values
    if (Object.values(FootwearStatus).includes(result as FootwearStatus)) {
      return result as FootwearStatus;
    }

    // Default to WEARING_FOOTWEAR if response is invalid
    return FootwearStatus.WEARING_FOOTWEAR;
  }
);
