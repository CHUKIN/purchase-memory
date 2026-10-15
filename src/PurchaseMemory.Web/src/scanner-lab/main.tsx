import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { ScannerLab } from "./ScannerLab";

createRoot(document.getElementById("root")!).render(<StrictMode><ScannerLab /></StrictMode>);
