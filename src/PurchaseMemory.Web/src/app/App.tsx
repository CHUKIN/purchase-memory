import { notifications } from "@mantine/notifications";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { getRecognitionStatus, getReviewQueueCount, listRecentReceipts } from "../api/client";
import type { ReceiptStatus } from "../api/types";
import { AnalyticsPage } from "../features/analytics/AnalyticsPage";
import { HelpPage } from "../features/help/HelpPage";
import { HomePage } from "../features/home/HomePage";
import { PurchaseDetailPage } from "../features/purchases/PurchaseDetailPage";
import { PurchasesPage } from "../features/purchases/PurchasesPage";
import { ProductDetailPage, ProductsPage } from "../features/products/ProductsPage";
import { CapturePage, ReceiptDetailPage } from "../features/receipts/CapturePage";
import { ReviewQueuePage } from "../features/review-queue/ReviewQueuePage";
import { reviewQueueCountQueryKey, reviewQueueQueryKey } from "../features/review-queue/reviewQueueQueries";
import { AppShell } from "./AppShell/AppShell";

export function App() {
  return <BrowserRouter><AppRoutes /></BrowserRouter>;
}

export function AppRoutes() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const recognitionQuery = useQuery({ queryKey: ["recognition"], queryFn: getRecognitionStatus, retry: false });
  const receiptsQuery = useQuery({
    queryKey: ["receipts", "recent"],
    queryFn: () => listRecentReceipts(20),
    refetchInterval: (query) => query.state.data?.some((receipt) => isActive(receipt.status)) ? 1_200 : false
  });
  // The badge refreshes after queue actions, when a receipt finishes, and once a minute.
  const reviewCountQuery = useQuery({ queryKey: reviewQueueCountQueryKey, queryFn: getReviewQueueCount, retry: false, staleTime: 30_000, refetchInterval: 60_000 });
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
          notifications.show({
            id: `receipt-${receipt.id}-${receipt.status}`,
            title: receipt.status === "failed" ? t("receiptJobs.failedNotification") : t("receiptJobs.readyNotification"),
            message: <span>{t("receiptJobs.openToContinue")} <a href={`/receipts/${receipt.id}`}>{t("receiptJobs.openReceipt")}</a></span>,
            color: receipt.status === "failed" ? "red" : "green"
          });
        }
      }
    }
    previousStatuses.current = next;
  }, [queryClient, receiptsQuery.data, t]);

  const recognition = recognitionQuery.data ?? null;
  const recognitionFailed = recognitionQuery.isError;
  const captureRecognition = recognitionFailed
    ? { available: false, mode: "unknown", models: [], message: null }
    : recognition;

  return (
    <AppShell recognition={recognition} recognitionFailed={recognitionFailed} reviewCount={reviewCountQuery.data?.totalCount ?? null}>
      <Routes>
        <Route path="/" element={<HomePage recognition={recognition} recognitionFailed={recognitionFailed} receipts={receiptsQuery.data ?? []} receiptsFailed={receiptsQuery.isError} />} />
        <Route path="/purchases" element={<PurchasesPage />} />
        <Route path="/purchases/new" element={<CapturePage recognition={captureRecognition} />} />
        <Route path="/purchases/:purchaseId" element={<PurchaseDetailPage />} />
        <Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} />
        <Route path="/products" element={<ProductsPage />} />
        <Route path="/products/:productId" element={<ProductDetailPage />} />
        <Route path="/analytics" element={<AnalyticsPage />} />
        <Route path="/needs-attention" element={<ReviewQueuePage />} />
        <Route path="/help" element={<HelpPage />} />
        <Route path="*" element={<Navigate replace to="/" />} />
      </Routes>
    </AppShell>
  );
}

function isActive(status: ReceiptStatus) {
  return status === "uploaded" || status === "queued" || status === "processing";
}

function isTerminal(status: ReceiptStatus) {
  return status === "ready" || status === "failed" || status === "saved";
}
