import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
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
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:receipt") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  });

  it("keeps gallery selection order and supports accessible reordering and removal", () => {
    render(<AppProviders><Harness /></AppProviders>);
    const top = new File(["top"], "top.jpg", { type: "image/jpeg" });
    const bottom = new File(["bottom"], "bottom.jpg", { type: "image/jpeg" });
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files: [top, bottom] } });

    let parts = screen.getAllByRole("listitem");
    expect(within(parts[0]).getByText("top.jpg")).toBeInTheDocument();
    expect(within(parts[1]).getByText("bottom.jpg")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Переместить часть 1 ниже" }));
    parts = screen.getAllByRole("listitem");
    expect(within(parts[0]).getByText("bottom.jpg")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Удалить часть 2" }));
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    expect(screen.queryByText("top.jpg")).not.toBeInTheDocument();
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
    expect(screen.getByRole("button", { name: "Remove receipt 2" })).toBeInTheDocument();
  });
});
