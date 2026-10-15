import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { chooseAction } from "../../test/ionicHelpers";
import { ReceiptCapture, type CaptureMode } from "./ReceiptCapture";

function Harness() {
  const [files, setFiles] = useState<File[]>([]);
  return <ReceiptCapture files={files} onFilesChange={setFiles} onSubmit={() => undefined} />;
}

function ModeHarness() {
  const [files, setFiles] = useState<File[]>([]);
  const [mode, setMode] = useState<CaptureMode>("parts");
  return <MemoryRouter><ReceiptCapture files={files} mode={mode} onModeChange={setMode} onFilesChange={setFiles} onSubmit={() => undefined} /></MemoryRouter>;
}

describe("production ReceiptCapture", () => {
  beforeEach(() => {
    // One URL per file, so a row's preview tells which photo it is.
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn((file: File) => `blob:${file.name}`) });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  });

  it("keeps gallery selection order and supports accessible reordering and removal", () => {
    render(<AppProviders><Harness /></AppProviders>);
    const top = new File(["top"], "top.jpg", { type: "image/jpeg" });
    const bottom = new File(["bottom"], "bottom.jpg", { type: "image/jpeg" });
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files: [top, bottom] } });

    // Rows show the photo, not its file name; the preview tells the parts apart.
    const photoOf = (part: HTMLElement) => within(part).getByRole("img").getAttribute("src");
    let parts = screen.getAllByRole("listitem");
    expect(photoOf(parts[0])).toBe("blob:top.jpg");
    expect(photoOf(parts[1])).toBe("blob:bottom.jpg");
    // A tap on a part offers its actions in an action sheet; the first part cannot move up.
    fireEvent.click(within(parts[0]).getByRole("button"));
    expect(screen.queryByRole("button", { name: "Переместить часть 1 выше" })).not.toBeInTheDocument();
    chooseAction("Переместить часть 1 ниже");
    parts = screen.getAllByRole("listitem");
    expect(photoOf(parts[0])).toBe("blob:bottom.jpg");
    fireEvent.click(within(parts[1]).getByRole("button"));
    chooseAction("Удалить часть 2");
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    expect(photoOf(screen.getAllByRole("listitem")[0])).toBe("blob:bottom.jpg");
  });

  it("exposes separate camera and multiple gallery inputs", () => {
    render(<AppProviders locale="en"><Harness /></AppProviders>);
    const camera = screen.getByLabelText("Take a photo") as HTMLInputElement;
    const gallery = screen.getByLabelText("Choose from gallery") as HTMLInputElement;
    expect(camera).toHaveAttribute("capture", "environment");
    expect(camera).not.toHaveAttribute("multiple");
    expect(gallery).toHaveAttribute("multiple");
  });

  it("does not silently discard a ninth selected part", () => {
    render(<AppProviders><Harness /></AppProviders>);
    const files = Array.from({ length: 9 }, (_, index) => new File([`${index}`], `part-${index + 1}.jpg`, { type: "image/jpeg" }));
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files } });

    expect(screen.getAllByRole("listitem")).toHaveLength(9);
    expect(screen.getByText("Можно добавить не больше 8 частей.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Загрузить и распознать" })).toBeDisabled();
  });

  it("asks what several photos are only once there are several", () => {
    render(<AppProviders locale="en"><ModeHarness /></AppProviders>);
    const gallery = screen.getByLabelText("Choose from gallery");
    fireEvent.change(gallery, { target: { files: [new File(["a"], "a.jpg", { type: "image/jpeg" })] } });
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();

    fireEvent.change(gallery, { target: { files: [new File(["b"], "b.jpg", { type: "image/jpeg" })] } });
    expect(screen.getByRole("radio", { name: /Parts of one receipt/ })).toBeChecked();
    expect(screen.getByRole("button", { name: "Upload and recognize" })).toBeEnabled();

    fireEvent.click(screen.getByRole("radio", { name: /Separate receipts/ }));
    expect(screen.getByText(/2 photos — 2 recognitions/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload 2 receipts" })).toBeEnabled();
    fireEvent.click(within(screen.getAllByRole("listitem")[1]).getByRole("button"));
    expect(screen.getByRole("button", { name: "Remove receipt 2" })).toBeInTheDocument();
    // Separate receipts have no order to change.
    expect(screen.queryByRole("button", { name: /Move part/ })).not.toBeInTheDocument();
  });
});
