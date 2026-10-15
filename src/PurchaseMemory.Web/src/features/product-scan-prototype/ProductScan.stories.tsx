import type { Meta, StoryObj } from "@storybook/react-vite";
import {
  AttachToLineSheet, HomeScanButton, HomeScanTiles, ProductCardCompact, ProductCardGallery, ScanResult,
  type ScanState, type ScanVariant
} from "./ProductScanMocks";

/*
 * Stage 1 prototype (no production code): pick a look for photos, barcodes and
 * scanning. Fake data, Russian only. See ПЛАН_ФОТО_И_ШТРИХКОД_ТОВАРА.md.
 */
const phone = { viewport: { value: "phone", isRotated: false } };

const meta = {
  title: "Prototype/Product photo and barcode",
  parameters: { layout: "fullscreen", a11y: { test: "todo" } },
  globals: { ...phone, locale: "ru" }
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

/** (a) Variant A: big gallery on top; tap a thumbnail, then «Сделать главным» or «Удалить фото». */
export const ProductCardGalleryA: Story = { render: () => <ProductCardGallery /> };
/** (a) Variant B: compact header photo, small photo strip, barcodes as chips. */
export const ProductCardCompactB: Story = { render: () => <ProductCardCompact /> };

/** (b) Variant 1: a second button under «Сфотографировать чек». */
export const HomeScanButtonVariant1: Story = { render: () => <HomeScanButton /> };
/** (b) Variant 2: two equal tiles. */
export const HomeScanTilesVariant2: Story = { render: () => <HomeScanTiles /> };

const resultArgs = {
  state: { control: "select", options: ["found", "foundAvoid", "notFound", "loading", "cameraDenied"] satisfies ScanState[] },
  variant: { control: "inline-radio", options: ["sheet", "full"] satisfies ScanVariant[] }
} as const;

/** (c) Playground: change state and variant in Controls. */
export const ScanResultPlayground: StoryObj<{ state: ScanState; variant: ScanVariant }> = {
  argTypes: resultArgs,
  args: { state: "found", variant: "sheet" },
  render: ({ state, variant }) => <ScanResult state={state} variant={variant} />
};

const result = (state: ScanState, variant: ScanVariant): Story => ({ render: () => <ScanResult state={state} variant={variant} /> });

export const ScanFoundSheet: Story = result("found", "sheet");
export const ScanFoundFull: Story = result("found", "full");
export const ScanFoundDoNotBuySheet: Story = result("foundAvoid", "sheet");
export const ScanFoundDoNotBuyFull: Story = result("foundAvoid", "full");
export const ScanNotFoundSheet: Story = result("notFound", "sheet");
export const ScanNotFoundFull: Story = result("notFound", "full");
export const ScanLoadingSheet: Story = result("loading", "sheet");
export const ScanCameraDeniedFull: Story = result("cameraDenied", "full");

/** (d) A purchase line already linked to a product: attach a photo or a barcode to it. */
export const AttachToLineLinked: Story = { render: () => <AttachToLineSheet linked /> };
/** (d) A line not linked yet: attaching starts with choosing the product. */
export const AttachToLineNotLinked: Story = { render: () => <AttachToLineSheet linked={false} /> };
