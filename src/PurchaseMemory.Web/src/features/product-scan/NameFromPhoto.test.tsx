import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, nameProductFromPhoto, setProductName, undoPhotoName } from "../../api/client";
import type { PhotoNameLanguageResult, ProductPhotoNameResult } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { dialog } from "../../test/ionicHelpers";
import { photoForNaming } from "./compressImage";
import { ProductMediaSection } from "./ProductMediaSection";
import { useNameFromPhoto } from "./useNameFromPhoto";
import { useProductMedia, useProductMediaActions } from "./useProductMedia";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  nameProductFromPhoto: vi.fn(),
  undoPhotoName: vi.fn(),
  setProductName: vi.fn()
}));

// jsdom cannot decode images; the shrink itself is plain canvas code.
vi.mock("./compressImage", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./compressImage")>()),
  photoForNaming: vi.fn(async (blob: Blob) => blob)
}));

vi.mock("./useProductMedia", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./useProductMedia")>()),
  useProductMedia: vi.fn(),
  useProductMediaActions: vi.fn(),
  usePhotoUrl: () => ({ url: null, failed: false })
}));

const photo = new Blob(["jpeg"], { type: "image/jpeg" });

function language(overrides: Partial<PhotoNameLanguageResult> & Pick<PhotoNameLanguageResult, "language" | "outcome">): PhotoNameLanguageResult {
  return { name: null, previousName: null, previousSource: null, ...overrides };
}

function result(languages: PhotoNameLanguageResult[]): ProductPhotoNameResult {
  return { productId: "k", namePl: null, namePlSource: null, nameRu: null, nameRuSource: null, languages };
}

function Harness({ automatic = false }: { automatic?: boolean }) {
  const naming = useNameFromPhoto("k");
  return (
    <>
      <button type="button" onClick={() => void naming.start(photo, { automatic })}>go</button>
      {naming.dialog}
    </>
  );
}

function renderHarness(automatic = false) {
  return render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <Harness automatic={automatic} />
      </MemoryRouter>
    </AppProviders>
  );
}

const toast = () => screen.findByRole("status");

describe("Name from photo", () => {
  beforeEach(() => {
    vi.mocked(nameProductFromPhoto).mockReset();
    vi.mocked(undoPhotoName).mockReset().mockResolvedValue({ productId: "k", namePl: null, namePlSource: null, nameRu: null, nameRuSource: null });
    vi.mocked(setProductName).mockReset().mockResolvedValue({ productId: "k", namePl: null, namePlSource: null, nameRu: null, nameRuSource: null });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("replaces suggested names, says so in a toast and «Вернуть» puts the previous ones back", async () => {
    vi.mocked(nameProductFromPhoto).mockResolvedValue(result([
      language({ language: "ru", outcome: "replaced", name: "Кефир натуральный Activia, 280 г", previousName: null }),
      language({ language: "pl", outcome: "replaced", name: "Kefir naturalny Activia 280 g", previousName: "Kefir Activia", previousSource: "suggested" })
    ]));
    renderHarness();

    fireEvent.click(screen.getByRole("button", { name: "go" }));

    const updated = await toast();
    expect(updated).toHaveTextContent("Название обновлено по фото: Kefir naturalny Activia 280 g · Кефир натуральный Activia, 280 г");
    expect(nameProductFromPhoto).toHaveBeenCalledWith("k", photo);
    expect(photoForNaming).toHaveBeenCalledWith(photo);
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();

    fireEvent.click(within(updated).getByRole("button", { name: "Вернуть" }));

    await waitFor(() => expect(undoPhotoName).toHaveBeenCalledTimes(2));
    expect(undoPhotoName).toHaveBeenCalledWith("k", { language: "pl", photoName: "Kefir naturalny Activia 280 g", previousName: "Kefir Activia" });
    expect(undoPhotoName).toHaveBeenCalledWith("k", { language: "ru", photoName: "Кефир натуральный Activia, 280 г", previousName: null });
    expect(await screen.findByText("Прежнее название возвращено.")).toBeInTheDocument();
  });

  it("says so quietly when the name changed before «Вернуть»", async () => {
    vi.mocked(nameProductFromPhoto).mockResolvedValue(result([
      language({ language: "pl", outcome: "replaced", name: "Kefir naturalny Activia 280 g", previousName: null })
    ]));
    vi.mocked(undoPhotoName).mockRejectedValue(new ApiError("changed", 409));
    renderHarness();

    fireEvent.click(screen.getByRole("button", { name: "go" }));
    fireEvent.click(within(await toast()).getByRole("button", { name: "Вернуть" }));

    expect(await screen.findByText("Не получилось вернуть: название уже изменили.")).toBeInTheDocument();
  });

  it("only proposes over a person's name and «Заменить» saves it as theirs", async () => {
    vi.mocked(nameProductFromPhoto).mockResolvedValue(result([
      language({ language: "pl", outcome: "proposed", name: "Kefir naturalny Activia 280 g", previousName: "Kefir", previousSource: "confirmed" }),
      language({ language: "ru", outcome: "unchanged", name: "Кефир" })
    ]));
    renderHarness();

    fireEvent.click(screen.getByRole("button", { name: "go" }));

    const alert = await waitFor(() => dialog("Предложить название"));
    expect(alert).toHaveTextContent("По фото: Kefir naturalny Activia 280 g. Заменить?");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    fireEvent.click(within(alert).getByRole("button", { name: "Заменить" }));

    await waitFor(() => expect(setProductName).toHaveBeenCalledWith("k", "pl", "Kefir naturalny Activia 280 g"));
    expect(setProductName).toHaveBeenCalledTimes(1);
  });

  it("«Отмена» on a proposal changes nothing", async () => {
    vi.mocked(nameProductFromPhoto).mockResolvedValue(result([
      language({ language: "ru", outcome: "proposed", name: "Кефир Activia", previousName: "Кефир", previousSource: "confirmed" }),
      language({ language: "pl", outcome: "notRead" })
    ]));
    renderHarness();

    fireEvent.click(screen.getByRole("button", { name: "go" }));
    fireEvent.click(within(await waitFor(() => dialog("Предложить название"))).getByRole("button", { name: "Отмена" }));

    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    expect(setProductName).not.toHaveBeenCalled();
  });

  it("a failed call is one quiet toast, and nothing is read from a photo it could not name", async () => {
    vi.mocked(nameProductFromPhoto).mockRejectedValueOnce(new ApiError("unavailable", 503, null, null, "nameFromPhotoUnavailable"));
    renderHarness();

    fireEvent.click(screen.getByRole("button", { name: "go" }));
    const failed = await toast();
    expect(failed).toHaveTextContent("Название по фото сейчас недоступно. Его можно ввести вручную.");
    expect(within(failed).queryByRole("button", { name: "Вернуть" })).not.toBeInTheDocument();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("says when the photo gave no name", async () => {
    vi.mocked(nameProductFromPhoto).mockResolvedValue(result([
      language({ language: "pl", outcome: "notRead" }),
      language({ language: "ru", outcome: "notRead" })
    ]));
    renderHarness();

    fireEvent.click(screen.getByRole("button", { name: "go" }));

    expect(await toast()).toHaveTextContent("На фото не получилось уверенно прочитать название.");
  });

  it("right after adding a photo offline it does not try at all", async () => {
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
    renderHarness(true);

    fireEvent.click(screen.getByRole("button", { name: "go" }));

    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(nameProductFromPhoto).not.toHaveBeenCalled();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});

describe("«Название по фото» on the product card", () => {
  const readPhoto = vi.fn();
  const addPhoto = vi.fn();

  function renderSection(photos: number) {
    vi.mocked(useProductMedia).mockReturnValue({
      data: {
        productId: "k",
        photos: Array.from({ length: photos }, (_, index) => ({ id: `p${index}`, createdAt: 1, width: 10, height: 10, size: 100 })),
        mainPhotoId: photos > 0 ? "p0" : null,
        barcodes: [],
        avoid: null
      }
    } as unknown as ReturnType<typeof useProductMedia>);
    // The server answers whether the upload was the product's first photo.
    addPhoto.mockReset().mockResolvedValue({ id: "new", firstPhoto: photos === 0 });
    vi.mocked(useProductMediaActions).mockReturnValue({ readPhoto, addPhoto } as unknown as ReturnType<typeof useProductMediaActions>);
    return render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductMediaSection productId="k" productName="Kefir" intro="1 покупка" locale="ru-RU" />
        </MemoryRouter>
      </AppProviders>
    );
  }

  beforeEach(() => {
    vi.mocked(nameProductFromPhoto).mockReset().mockResolvedValue(result([
      language({ language: "pl", outcome: "replaced", name: "Kefir naturalny Activia 280 g" })
    ]));
    readPhoto.mockReset().mockResolvedValue(photo);
  });

  const pickFile = () => fireEvent.change(screen.getByLabelText("Из галереи"), { target: { files: [new File(["x"], "a.jpg", { type: "image/jpeg" })] } });

  it("names the product from its first photo right away", async () => {
    renderSection(0);
    pickFile();

    await waitFor(() => expect(nameProductFromPhoto).toHaveBeenCalledTimes(1));
    expect(addPhoto).toHaveBeenCalledTimes(1);
  });

  it("follows the server: no automatic name when the other member added a photo meanwhile", async () => {
    renderSection(0);
    addPhoto.mockResolvedValue({ id: "new", firstPhoto: false });
    pickFile();

    await waitFor(() => expect(addPhoto).toHaveBeenCalledTimes(1));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(nameProductFromPhoto).not.toHaveBeenCalled();
  });

  it("never renames from a later photo; the button still does it on demand", async () => {
    renderSection(1);
    pickFile();

    await waitFor(() => expect(addPhoto).toHaveBeenCalledTimes(1));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(nameProductFromPhoto).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Название по фото" }));
    await waitFor(() => expect(nameProductFromPhoto).toHaveBeenCalledTimes(1));
  });

  it("is there only when the product has a photo, and reads the main one", async () => {
    const { unmount } = renderSection(0);
    expect(screen.queryByRole("button", { name: "Название по фото" })).not.toBeInTheDocument();
    unmount();

    renderSection(2);
    fireEvent.click(screen.getByRole("button", { name: "Название по фото" }));

    await waitFor(() => expect(nameProductFromPhoto).toHaveBeenCalledWith("k", photo));
    expect(readPhoto).toHaveBeenCalledWith("p0");
    expect(await toast()).toHaveTextContent("Название обновлено по фото: Kefir naturalny Activia 280 g");
  });
});
