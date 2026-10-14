import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(async () => {
  cleanup();
  // Swipe-review marks: no timers or listeners carry over to the next test.
  // Imported late, so it is the module the test loaded (with its API mocks).
  const { resetApprovalSyncs } = await import("../features/line-review/approvalSync");
  resetApprovalSyncs();
  window.localStorage.clear();
});

Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false
  })
});
