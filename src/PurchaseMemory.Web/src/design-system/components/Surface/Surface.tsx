import type { ElementType, HTMLAttributes, PropsWithChildren } from "react";
import styles from "./Surface.module.css";

export type SurfaceTone = "paper" | "soft" | "warm";
export type SurfaceDensity = "comfortable" | "compact";

export interface SurfaceProps extends PropsWithChildren<HTMLAttributes<HTMLElement>> {
  as?: "article" | "section" | "div" | "aside";
  tone?: SurfaceTone;
  density?: SurfaceDensity;
  elevated?: boolean;
}

export function Surface({
  as = "div",
  tone = "paper",
  density = "comfortable",
  elevated = false,
  className,
  children,
  ...props
}: SurfaceProps) {
  const Component = as as ElementType;
  return (
    <Component
      {...props}
      className={[
        styles.surface,
        styles[tone],
        styles[density],
        elevated ? styles.elevated : "",
        className
      ].filter(Boolean).join(" ")}
    >
      {children}
    </Component>
  );
}
