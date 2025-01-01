import { onFlow } from "@genkit-ai/firebase/functions";
import { firebaseAuth } from "@genkit-ai/firebase/auth";
import { ai, googleAIapiKey } from "../../utils/genkit-setup";
import { getShoeNames } from "../../utils/firestore";
import { ShoeDetectionInputSchema } from "./types";
import { z } from "genkit";

// Tool to fetch shoes from Firestore
const getShoesList = ai.defineTool(
  {
    name: "getShoesList",
    description: "Gets the list of all shoe names from the database",
    inputSchema: z.object({
      dummy: z
        .boolean()
        .optional()
        .describe("Dummy parameter to satisfy API requirements"),
    }),
    outputSchema: z.array(z.string()),
  },
  getShoeNames
);

export const detectShoe = onFlow(
  ai,
  {
    name: "detectShoe",
    inputSchema: ShoeDetectionInputSchema,
    outputSchema: z.string(),
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
    const systemPrompt = `You are a shoe detection expert. Your task is to identify shoes in images and find the best match from our database.
              
              Follow these guidelines:
              1. If you see a shoe in the image, use the getShoesList tool to fetch the list of known shoes.
              2. Try to find the closest match from the database, considering:
                 - The overall style and design of the shoe
                 - The brand if visible
                 - The color scheme (though color alone shouldn't determine a match)
                 - Distinctive features or patterns
              3. Return the best matching shoe name from the database if you're reasonably confident (70% or higher similarity)
              4. Only return "UNKNOWN_SHOE" if:
                 - The shoe is clearly different from all options in the database
                 - You can't make out enough details to make a reasonable match
              5. Return "SHOE_NOT_FOUND" only if there is no shoe visible in the image
              
              IMPORTANT: Your response must be EXACTLY one of:
              - A shoe name that exactly matches one from the database
              - "UNKNOWN_SHOE"
              - "SHOE_NOT_FOUND"
              
              DO NOT include any additional text, explanations, or sentences. Just return the exact match or status.`;

    const { text } = await ai.generate({
      prompt: [
        { text: systemPrompt },
        { media: { url: `data:image/jpeg;base64,${data}` } },
        {
          text: "What is the name of this shoe? Use the getShoesList tool and return ONLY the exact matching name or status.",
        },
      ],
      tools: [getShoesList],
    });

    // Clean and extract the result, handling potential sentence responses
    let result = text.trim();

    // Remove any quotes
    result = result.replace(/^["']|["']$/g, "");

    // Try to extract just the shoe name if it's embedded in a sentence
    const shoes = await getShoesList({});
    for (const shoe of shoes) {
      if (result.includes(shoe)) {
        result = shoe;
        break;
      }
    }

    // If no shoe name was found in the text, check for status keywords
    if (!shoes.includes(result)) {
      if (result.includes("UNKNOWN_SHOE")) {
        result = "UNKNOWN_SHOE";
      } else if (result.includes("SHOE_NOT_FOUND")) {
        result = "SHOE_NOT_FOUND";
      }
    }

    // Log the cleaned result
    console.log("Cleaned shoe detection result:", result);
    console.log("Number of shoes in database:", shoes.length);

    // Final validation
    if (
      !["UNKNOWN_SHOE", "SHOE_NOT_FOUND"].includes(result) &&
      !shoes.includes(result)
    ) {
      return "UNKNOWN_SHOE";
    }

    return result;
  }
);
