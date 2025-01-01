import { onFlow } from "@genkit-ai/firebase/functions";
import { firebaseAuth } from "@genkit-ai/firebase/auth";
import { ai, googleAIapiKey } from "../../utils/genkit-setup";
import { getShoeNames } from "../../utils/firestore";
import { ShoeDetectionInputSchema } from "./types";
import { z } from "genkit";

// Define the output schema for shoe detection
const ShoeDetectionOutputSchema = z.object({
  name: z.string(),
  documentId: z.string(),
});

// Tool to fetch shoes from Firestore with their document IDs
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
  getShoeNames
);

export const detectShoe = onFlow(
  ai,
  {
    name: "detectShoe",
    inputSchema: ShoeDetectionInputSchema,
    outputSchema: ShoeDetectionOutputSchema,
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
      if (result.includes(shoe.name)) {
        return {
          name: shoe.name,
          documentId: shoe.documentId,
        };
      }
    }

    // Handle special cases
    if (result.includes("UNKNOWN_SHOE")) {
      return {
        name: "UNKNOWN_SHOE",
        documentId: "0", // Special case for unknown shoe
      };
    } else if (result.includes("SHOE_NOT_FOUND")) {
      return {
        name: "SHOE_NOT_FOUND",
        documentId: "-1", // Special case for no shoe found
      };
    }

    // If no match found, return as unknown shoe
    return {
      name: "UNKNOWN_SHOE",
      documentId: "0",
    };
  }
);
