import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router";
import { describe, expect, it } from "vitest";
import { useUrlFilters } from "./useUrlFilters";

const defaults = { query: "" };

function Probe() {
  const [filters, update] = useUrlFilters(defaults);
  const location = useLocation();
  return (
    <>
      <button type="button" onClick={() => update({ query: "kawa" })}>filter</button>
      <p data-testid="state">{JSON.stringify(location.state)}</p>
      <p data-testid="query">{filters.query}</p>
    </>
  );
}

describe("useUrlFilters", () => {
  it("keeps the entry's router state (the way back) when a filter changes", () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: "/purchases", state: { returnTo: "/products/p1", returnKey: "k" } }]}>
        <Probe />
      </MemoryRouter>
    );

    fireEvent.click(screen.getByRole("button", { name: "filter" }));
    expect(screen.getByTestId("query")).toHaveTextContent("kawa");
    expect(JSON.parse(screen.getByTestId("state").textContent ?? "null")).toEqual({ returnTo: "/products/p1", returnKey: "k" });
  });
});
