import { IonSpinner } from "@ionic/react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Suspense, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { BrowserRouter, Navigate, Route, Routes, useLocation } from "react-router";
import { getRecognitionStatus, listRecentReceipts } from "../api/client";
import type { ReceiptStatus, RecognitionStatus } from "../api/types";
import { DeviceMediaMover } from "../features/product-scan/DeviceMediaMover";
import { reviewQueueQueryKey } from "../features/review-queue/reviewQueueQueries";
import { useReviewCount } from "../features/review-queue/useReviewCount";
import { AppShell } from "./AppShell/AppShell";
import { PlaceMemory } from "./PlaceMemory";
import { lazyPage, ScreenErrorBoundary } from "./lazyPage";
import { ToastNavigationBridge, useToast } from "./toasts";

// One chunk per screen: the first paint only needs the shell and Home.
const HomePage = lazyPage(() => import("../features/home/HomePage").then((module) => ({ default: module.HomePage })));
const PurchasesPage = lazyPage(() => import("../features/purchases/PurchasesPage").then((module) => ({ default: module.PurchasesPage })));
const PurchaseDetailPage = lazyPage(() => import("../features/purchases/PurchaseDetailPage").then((module) => ({ default: module.PurchaseDetailPage })));
const CapturePage = lazyPage(() => import("../features/receipts/CapturePage").then((module) => ({ default: module.CapturePage })));
const ReceiptDetailPage = lazyPage(() => import("../features/receipts/ReceiptDetailPage").then((module) => ({ default: module.ReceiptDetailPage })));
const ProductsPage = lazyPage(() => import("../features/products/ProductsPage").then((module) => ({ default: module.ProductsPage })));
const ProductDetailPage = lazyPage(() => import("../features/products/ProductDetailPage").then((module) => ({ default: module.ProductDetailPage })));
const AnalyticsPage = lazyPage(() => import("../features/analytics/AnalyticsPage").then((module) => ({ default: module.AnalyticsPage })));
const ReviewQueuePage = lazyPage(() => import("../features/review-queue/ReviewQueuePage").then((module) => ({ default: module.ReviewQueuePage })));
// The barcode decoder is a further lazy import inside this screen, on first camera start.
const ScanPage = lazyPage(() => import("../features/product-scan/ScanPage").then((module) => ({ default: module.ScanPage })));
const HelpPage = lazyPage(() => import("../features/help/HelpPage").then((module) => ({ default: module.HelpPage })));

export function App() {
  return <BrowserRouter><AppRoutes /></BrowserRouter>;
}

export function AppRoutes() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const showToast = useToast();
  const location = useLocation();
  const recognitionQuery = useQuery({ queryKey: ["recognition"], queryFn: getRecognitionStatus, retry: false });
  const receiptsQuery = useQuery({
    queryKey: ["receipts", "recent"],
    queryFn: () => listRecentReceipts(20),
    refetchInterval: (query) => query.state.data?.some((receipt) => isActive(receipt.status)) ? 1_200 : false
  });
  // One count for the tab badge and the Home card (see useReviewCount).
  const reviewCount = useReviewCount();
  const previousStatuses = useRef<Map<string, ReceiptStatus> | null>(null);

  useEffect(() => {
    const receipts = receiptsQuery.data;
    if (!receipts) return;
    const next = new Map(receipts.map((receipt) => [receipt.id, receipt.status]));
    if (previousStatuses.current) {
      for (const receipt of receipts) {
        const previous = previousStatuses.current.get(receipt.id);
        if (previous && isActive(previous) && isTerminal(receipt.status)) {
          void queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey });
          const failed = receipt.status === "failed";
          showToast({
            id: `receipt-${receipt.id}-${receipt.status}`,
            header: failed ? t("receiptJobs.failedNotification") : t("receiptJobs.readyNotification"),
            message: t("receiptJobs.openToContinue"),
            tone: failed ? "danger" : "success",
            action: { label: t("receiptJobs.openReceipt"), to: `/receipts/${receipt.id}` }
          });
        }
      }
    }
    previousStatuses.current = next;
  }, [queryClient, receiptsQuery.data, showToast, t]);

  const recognition = recognitionQuery.data ?? null;
  const recognitionFailed = recognitionQuery.isError;
  const captureRecognition: RecognitionStatus | null = recognitionFailed
    ? { available: false, mode: "unknown", models: [], message: null }
    : recognition;

  return (
    <AppShell reviewCount={reviewCount.total}>
      <ToastNavigationBridge />
      <DeviceMediaMover />
      <PlaceMemory />
      <ScreenErrorBoundary resetKey={location.pathname}>
        <Suspense fallback={<PageLoading />}>
          <Routes>
            <Route path="/" element={<HomePage recognition={recognition} recognitionFailed={recognitionFailed} receipts={receiptsQuery.data ?? []} receiptsFailed={receiptsQuery.isError} reviewCount={reviewCount.total} />} />
            <Route path="/purchases" element={<PurchasesPage />} />
            <Route path="/purchases/new" element={<CapturePage recognition={captureRecognition} />} />
            <Route path="/purchases/:purchaseId" element={<PurchaseDetailPage />} />
            <Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} />
            <Route path="/products" element={<ProductsPage />} />
            <Route path="/products/:productId" element={<ProductDetailPage />} />
            <Route path="/analytics" element={<AnalyticsPage />} />
            <Route path="/needs-attention" element={<ReviewQueuePage />} />
            <Route path="/scan" element={<ScanPage />} />
            <Route path="/help" element={<HelpPage />} />
            <Route path="*" element={<Navigate replace to="/" />} />
          </Routes>
        </Suspense>
      </ScreenErrorBoundary>
    </AppShell>
  );
}

/** While a screen's chunk loads: an empty grouped page, so the tab bar does not jump. */
function PageLoading() {
  const { t } = useTranslation();
  return (
    <div className="ion-page pm-page-loading" role="status" aria-label={t("ui.loading")}>
      <IonSpinner name="crescent" />
    </div>
  );
}

function isActive(status: ReceiptStatus) {
  return status === "uploaded" || status === "queued" || status === "processing";
}

function isTerminal(status: ReceiptStatus) {
  return status === "ready" || status === "failed" || status === "saved";
}
