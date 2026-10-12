import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { ReceiptView } from "../../api/types";
import { InterruptedBatchNotice, RecentUploadsList } from "./BatchRecovery";
import type { RestoredBatch, StoredBatchPhoto } from "./pendingBatchStore";

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const receipt = (id: string, status: ReceiptView["status"], merchantName: string | null, warnings: string[] = []): ReceiptView => ({
  id, status, uploadedAt: "2026-10-12T18:42:00Z", updatedAt: "2026-10-12T18:43:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null,
  snapshot: merchantName ? { merchantName, purchasedAt: null, currency: "PLN", receiptTotal: 12, items: [] } : null,
  warnings, issues: [], error: null, savedPurchaseId: null
});
const recent = [
  receipt("1f0e4a3c-0000-4000-8000-000000000001", "processing", null),
  receipt("1f0e4a3c-0000-4000-8000-000000000002", "ready", "Biedronka Polska Sp. z o.o. — Warszawa, ul. Marszałkowska"),
  receipt("1f0e4a3c-0000-4000-8000-000000000003", "ready", "Lidl", ["Store not recognized"]),
  receipt("1f0e4a3c-0000-4000-8000-000000000004", "failed", null),
  receipt("1f0e4a3c-0000-4000-8000-000000000005", "uploaded", null)
];

const stored = (name: string): StoredBatchPhoto => ({ name, type: "image/jpeg", size: 3_400_000, lastModified: 0, sent: false, kept: true });
const file = (name: string) => new File(["x"], name, { type: "image/jpeg" });
const restored = (kept: string[], lost: string[]): RestoredBatch => ({
  batch: { userId: null, startedAt: 0, photos: [...kept, ...lost].map(stored) },
  files: kept.map(file),
  lost: lost.map((name) => ({ ...stored(name), kept: false }))
});

const meta = {
  title: "Purchase Memory/ReceiptCapture/Batch recovery",
  component: InterruptedBatchNotice,
  parameters: { layout: "padded" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  args: { restored: restored(["IMG_4512.jpeg", "IMG_4513.jpeg", "IMG_4514.jpeg"], []), disabled: false, onResume: fn(), onDiscard: fn() }
} satisfies Meta<typeof InterruptedBatchNotice>;
export default meta;
type Story = StoryObj<typeof meta>;

export const InterruptedMobileRu: Story = { globals: phone };
export const InterruptedWithLostMobileRu: Story = { globals: phone, args: { restored: restored(["IMG_4512.jpeg"], ["IMG_4513_very_long_file_name_from_the_photo_library.jpeg", "IMG_4514.jpeg"]) } };
export const AllLostMobileEn: Story = { globals: { ...phone, locale: "en" }, args: { restored: restored([], ["IMG_4513.jpeg"]) } };
export const InterruptedDesktopEn: Story = { globals: { ...desktop, locale: "en" } };
export const RecentUploadsMobileRu: Story = { globals: phone, render: () => <RecentUploadsList receipts={recent} /> };
export const RecentUploadsDesktopEn: Story = { globals: { ...desktop, locale: "en" }, render: () => <RecentUploadsList receipts={recent} /> };
