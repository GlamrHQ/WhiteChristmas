import { z } from "genkit";

export const ShoeDetectionInputSchema = z.object({
  data: z.string().describe("Base64 encoded image data"),
});

export type ShoeDetectionInput = z.infer<typeof ShoeDetectionInputSchema>;

export type ShoeDetectionResult = string; // Name of shoe, "UNKNOWN_SHOE", or "SHOE_NOT_FOUND"
