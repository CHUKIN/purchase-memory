import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { MobileHome } from "./MobileHome";

describe("MobileHome", () => {
  it("keeps the four primary sections in compact navigation", () => {
    render(<AppProviders><MobileHome /></AppProviders>);
    const navigation = screen.getByRole("navigation", { name: "Основная навигация для телефона" });
    expect(within(navigation).getByRole("link", { name: "Главная" })).toBeInTheDocument();
    expect(within(navigation).getByRole("link", { name: "Покупки" })).toBeInTheDocument();
    expect(within(navigation).getByRole("link", { name: "Товары" })).toBeInTheDocument();
    expect(within(navigation).getByRole("link", { name: "Аналитика" })).toBeInTheDocument();
  });
});
