import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import type { ReceiptStatus, ReceiptView } from "../../api/types";
import { ReceiptProgress } from "./ReceiptProgress";

const fixture = (status: ReceiptStatus, warnings: string[] = []): ReceiptView => ({
  id: "1fcb520a-c083-45b9-905d-d568bfef4f79", status, uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/receipt.svg", imageParts: [
    { position: 0, contentType: "image/jpeg", sizeBytes: 1200000, sourceImageUrl: "/receipt.svg" },
    { position: 1, contentType: "image/jpeg", sizeBytes: 1100000, sourceImageUrl: "/receipt.svg" }
  ], provider: "openai", model: "gpt-5", processingDurationMs: null,
  snapshot: status === "ready" ? { merchantName: "Biedronka", purchasedAt: "2026-08-03T11:00:00Z", currency: "PLN", receiptTotal: 42.5, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 4.5, lineTotal: 4.5 }] } : null,
  warnings, issues: [], error: status === "failed" ? "The provider could not be reached." : null, savedPurchaseId: null
});
const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };
const meta = { title: "Purchase Memory/ReceiptProgress/Production", component: ReceiptProgress, parameters: { layout: "padded" }, args: { receipt: fixture("queued"), onRetry: fn() } } satisfies Meta<typeof ReceiptProgress>;
export default meta;
type Story = StoryObj<typeof meta>;
export const QueuedMobileRu: Story = { globals: phone };
export const ProcessingMobileEn: Story = { globals: { ...phone, locale: "en" }, args: { receipt: fixture("processing") } };
export const ReadyDesktopRu: Story = { globals: desktop, args: { receipt: fixture("ready") } };
export const NeedsAttentionDesktopEn: Story = { globals: { ...desktop, locale: "en" }, args: { receipt: fixture("ready", ["Receipt lines do not match the total."]) } };
export const ProviderErrorMobileRu: Story = { globals: phone, args: { receipt: fixture("failed") } };
