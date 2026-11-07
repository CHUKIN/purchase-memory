import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { ScanPage } from "./ScanPage";
import { useRememberedBarcodes } from "./useProductMedia";

vi.mock("./BarcodeCamera", () => ({ BarcodeCamera: () => <div data-testid="camera" /> }));
vi.mock("./useProductMedia", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./useProductMedia")>()),
  useRememberedBarcodes: vi.fn(),
  useBarcodeOwner: () => ({ data: null, isPending: false, isError: false, refetch: vi.fn() })
}));

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname + location.search}</output>;
}

function renderScan() {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={["/scan"]}>
        <Routes>
          <Route path="/scan" element={<ScanPage />} />
          <Route path="*" element={null} />
        </Routes>
        <Where />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("ScanPage remembered codes", () => {
  beforeEach(() => vi.mocked(useRememberedBarcodes).mockReset());

  it("links to the remembered codes (a section of «Ревью») with their count", () => {
    vi.mocked(useRememberedBarcodes).mockReturnValue({ data: [{ code: "1" }, { code: "2" }] } as never);
    renderScan();

    fireEvent.click(screen.getByRole("button", { name: /Запомненные коды \(2\)/ }));
    expect(screen.getByTestId("where")).toHaveTextContent("/needs-attention?open=barcodes");
  });

  it("shows no link when nothing is remembered", () => {
    vi.mocked(useRememberedBarcodes).mockReturnValue({ data: [] } as never);
    renderScan();

    expect(screen.queryByRole("button", { name: /Запомненные коды/ })).not.toBeInTheDocument();
  });
});
