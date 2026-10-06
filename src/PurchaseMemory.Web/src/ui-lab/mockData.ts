import type { PurchaseCardData } from "./PurchaseCard/PurchaseCard";

export const recentPurchases: PurchaseCardData[] = [
  {
    id: "purchase-biedronka",
    merchant: "Biedronka",
    location: "ul. Grójecka 194, Warszawa",
    purchasedAt: "2026-08-03T17:42:00+02:00",
    total: 86.47,
    currency: "PLN",
    itemCount: 9,
    status: "verified",
    shopper: "Ania",
    items: [
      { name: "Mleko Łaciate 3,2%", total: 4.79 },
      { name: "Chleb żytni", total: 6.49 },
      { name: "Borówki 300 g", total: 12.99 }
    ]
  },
  {
    id: "purchase-lidl",
    merchant: "Lidl",
    location: "al. Jerozolimskie 179, Warszawa",
    purchasedAt: "2026-08-02T19:08:00+02:00",
    total: 124.32,
    currency: "PLN",
    itemCount: 14,
    status: "needsAttention",
    shopper: "Michał",
    issueItem: "Kawa ziarnista Bellarom",
    items: [
      { name: "Kawa ziarnista Bellarom", total: 39.99 },
      { name: "Pomidory malinowe", total: 9.84 },
      { name: "Mozzarella", total: 6.49 }
    ]
  },
  {
    id: "purchase-carrefour",
    merchant: "Carrefour",
    location: "CH Arkadia, Warszawa",
    purchasedAt: "2026-08-01T16:20:00+02:00",
    total: 58.16,
    currency: "PLN",
    itemCount: 7,
    status: "possibleDuplicate",
    shopper: "Ania",
    items: [
      { name: "Makaron pełnoziarnisty", total: 8.59 },
      { name: "Passata pomidorowa", total: 5.99 }
    ]
  }
];
