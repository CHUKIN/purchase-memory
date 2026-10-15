import basicSsl from "@vitejs/plugin-basic-ssl";
import { mergeConfig } from "vite";
import config from "./vite.config";

/**
 * `npm run dev:lab`: the normal dev server plus a self-signed HTTPS certificate,
 * so iPhone Safari (which gives the camera to secure pages only) can open
 * https://<Mac IP>:5173/scanner-lab.html over the local network. Used only for
 * the throwaway scanner page; `npm run dev` and `npm run build` never load this file.
 */
export default mergeConfig(config, { plugins: [basicSsl()] });
