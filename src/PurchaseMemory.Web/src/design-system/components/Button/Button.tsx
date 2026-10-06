import { Button as MantineButton } from "@mantine/core";
import type { ComponentPropsWithoutRef, ReactNode } from "react";
import styles from "./Button.module.css";

export type ButtonIntent = "primary" | "secondary" | "quiet" | "danger";

export interface ButtonProps extends Omit<ComponentPropsWithoutRef<"button">, "color"> {
  intent?: ButtonIntent;
  loading?: boolean;
  fullWidth?: boolean;
  leftSection?: ReactNode;
  rightSection?: ReactNode;
}

const intentProps = {
  primary: { variant: "filled", color: "forest" },
  secondary: { variant: "light", color: "forest" },
  quiet: { variant: "subtle", color: "forest" },
  danger: { variant: "light", color: "red.8" }
} as const;

export function Button({
  intent = "primary",
  loading = false,
  disabled,
  fullWidth,
  leftSection,
  rightSection,
  type = "button",
  className,
  children,
  ...props
}: ButtonProps) {
  return (
    <MantineButton
      {...props}
      {...intentProps[intent]}
      type={type}
      loading={loading}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      fullWidth={fullWidth}
      leftSection={leftSection}
      rightSection={rightSection}
      className={[styles.button, className].filter(Boolean).join(" ")}
    >
      {children}
    </MantineButton>
  );
}
