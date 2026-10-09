import { Loader, Tooltip } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import type { ComponentPropsWithRef, ReactNode } from "react";
import type { ButtonIntent } from "../Button/Button";
import styles from "./IconButton.module.css";

/**
 * Where the text label is hidden. "mobile" keeps the text on wider screens,
 * where there is room for it; "always" is for actions whose icon is obvious
 * everywhere (delete a row, close).
 */
export type IconButtonIconOnly = "always" | "mobile";

export interface IconButtonProps extends Omit<ComponentPropsWithRef<"button">, "children" | "aria-label"> {
  /** Accessible name, tooltip (unless `tooltip` is set) and, when shown, the visible text. Always pass the translated action. */
  label: string;
  /** Shorter visible text when the label carries context, e.g. label «Удалить: акция», text «Удалить». */
  text?: string;
  /** Shorter tooltip when the label carries long context, e.g. label «Удалить вопрос: …», tooltip «Удалить». */
  tooltip?: string;
  icon: ReactNode;
  intent?: ButtonIntent;
  iconOnly?: IconButtonIconOnly;
  loading?: boolean;
}

/** Matches the CSS breakpoint below which "mobile" icon buttons drop their text. */
export const iconButtonWideQuery = "(min-width: 40em)";

/**
 * A secondary action drawn as an icon on narrow screens. The button keeps a
 * 44 px touch target, its translated name as `aria-label` (so screen readers
 * and tests see the same name as the old text button), and a tooltip on hover
 * or keyboard focus whenever the text is hidden.
 */
export function IconButton({
  label,
  text = label,
  tooltip = label,
  icon,
  intent = "quiet",
  iconOnly = "always",
  loading = false,
  disabled,
  type = "button",
  className,
  ref,
  ...props
}: IconButtonProps) {
  const wide = useMediaQuery(iconButtonWideQuery);
  const textVisible = iconOnly === "mobile" && wide === true;

  return (
    <Tooltip label={tooltip} disabled={textVisible} events={{ hover: true, focus: true, touch: false }} openDelay={250} withArrow>
      <button
        {...props}
        ref={ref}
        type={type}
        aria-label={label}
        aria-busy={loading || undefined}
        disabled={disabled || loading}
        data-intent={intent}
        data-icon-only={iconOnly}
        className={[styles.iconButton, className].filter(Boolean).join(" ")}
      >
        <span className={styles.icon} aria-hidden="true">
          {loading ? <Loader size={16} color="currentColor" /> : icon}
        </span>
        {iconOnly === "mobile" && <span className={styles.text} aria-hidden="true">{text}</span>}
      </button>
    </Tooltip>
  );
}
