import { describe, expect, it } from "vitest";
import { findStoreLogo, normalizeMerchant } from "./storeLogos";

describe("findStoreLogo", () => {
  it.each([
    ["E.Leclerc URSYNÓW", "leclerc"],
    ["E. LECLERC", "leclerc"],
    ["Rossmann SDP Sp. z o.o.", "rossmann"],
    ["ROSSMANN", "rossmann"],
    ["Lidl Sp. z o.o. sp.k.", "lidl"],
    ["ŻABKA Z1234", "zabka"],
    ["Żabka Polska", "zabka"],
    ["Auchan Polska Sp. z o.o.", "auchan"],
    ["PEPCO Poland", "pepco"],
    ["Carrefour", "carrefour"],
    ["Carrefour Express", "carrefour"],
    ["Carrefour Market Warszawa", "carrefour"]
  ])("%s -> %s", (name, id) => {
    expect(findStoreLogo(name)?.id).toBe(id);
  });

  it.each(["Lidlmarket", "Gelidl", "Carrefourx","Biedronka", "Sklep u Janka", "", "   ", null, undefined])(
    "no logo for %s",
    (name) => {
      expect(findStoreLogo(name)).toBeNull();
    }
  );

  it("folds diacritics and punctuation", () => {
    expect(normalizeMerchant("  E.Leclerc ŁÓDŹ ")).toBe("e leclerc lodz");
  });
});
