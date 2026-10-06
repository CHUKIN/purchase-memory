import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import { ReceiptCapture } from "./ReceiptCapture";
import { ReceiptDuplicateNotice } from "./ReceiptDuplicateNotice";
import { MemoryRouter } from "react-router";

const receiptPng = "iVBORw0KGgoAAAANSUhEUgAAAKAAAADcCAIAAAB4TT3AAAACOklEQVR42u3cIW4CURSG0Z+m+0HUdQXsANEQVE3LctoaFCEV7IAV4BDsAo/AsgACYZI3YebNObKi4n29j4FLOjqfjqFeL45AYARGYASmvNfrHx32O+fSU+O3dxM8+Am+9bdAl926d02whywERmAERmAERmCBERiBERiBSSvbpKYm05nTLGu7WZtgBBYYgREYgREYgREYgQVGYAQmlWyTiqw+MMEIjMACIzACU8/bpMf9r5ZDPvGP+acJRmAERmCB8RTd8cdIE4zACIzACIzACIzAAiMwAiMwAiMwAguMwAhM6vjKzvfXwvFd+/37afUAH//9JtgVjcAITD8fspq+2vOUAzTBrmgERmAERmAERmCBEZjYB5M7n1PaB+OKRmCB8ZBlH2wfjCsagREYgRFYYARGYAQm9sGp8WNF+2Bc0QgsMB6y7IPtg3FFIzACIzACC4zACIzAxD44NX4waR+MKxqBBcZDln2wfTCuaARGYARGYIERGIERGIERGIEFRmAERmAEJr4XHV/rMcGuaEcgMALje9GYYARGYAQWGIERGIERGIGJfXD6/s8YTDACC4zA2AdjghEYgRFYYARGYARGYAQm9sGxUTbBrmgERmDsgzHBCIzAAiMwAiMwAiMwAsdHlbRvMp2V+lXbzdoEU2iCC/5hdkfTEfEajMAIjMAILDACM5j3wZW9ZRSY/k2CK9prMAIjMAIjMAIjsMAIjMAITJ61bDjsd07HBNN1o/Pp6BRMMAIjMAIjME1cAL/cV1FbEe54AAAAAElFTkSuQmCC";
const receiptBytes = Uint8Array.from(atob(receiptPng), (character) => character.charCodeAt(0));
const photo = (name: string) => new File([receiptBytes], name, { type: "image/png" });
const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };
const meta = {
  title: "Purchase Memory/ReceiptCapture/Production",
  component: ReceiptCapture,
  parameters: { layout: "padded" },
  args: { files: [], onFilesChange: fn(), onSubmit: fn() }
} satisfies Meta<typeof ReceiptCapture>;
export default meta;
type Story = StoryObj<typeof meta>;

export const EmptyMobileRu: Story = { globals: phone };
export const SingleMobileEn: Story = { globals: { ...phone, locale: "en" }, args: { files: [photo("receipt-top.png")] } };
export const MultipartMobileRu: Story = { globals: phone, args: { files: [photo("receipt-top.png"), photo("receipt-middle.png"), photo("receipt-bottom.png")] } };
export const UploadingDesktopEn: Story = { globals: { ...desktop, locale: "en" }, args: { files: [photo("receipt.png")], uploading: true } };
export const ExactDuplicateDesktopRu: Story = { globals: desktop, render: () => <MemoryRouter><ReceiptDuplicateNotice receiptId="1fcb520a-c083-45b9-905d-d568bfef4f79" onStartOver={fn()} /></MemoryRouter> };
