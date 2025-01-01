import { z } from "genkit";
import { FootwearStatus } from "../../utils/genkit-setup";

export const FootMeasurementInputSchema = z.object({
  data: z.string().describe("Base64 encoded image data"),
});

export type FootMeasurementInput = z.infer<typeof FootMeasurementInputSchema>;

export const FootMeasurementOutputSchema = z.nativeEnum(FootwearStatus);
