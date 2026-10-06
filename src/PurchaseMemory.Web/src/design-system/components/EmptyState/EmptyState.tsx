import type { ReactNode } from "react";
import styles from "./EmptyState.module.css";

export interface EmptyStateProps {
  icon: ReactNode;
  title: string;
  description: string;
  action?: ReactNode;
  headingLevel?: 1 | 2 | 3;
}

export function EmptyState({ icon, title, description, action, headingLevel = 3 }: EmptyStateProps) {
  const Heading = headingLevel === 1 ? "h1" : headingLevel === 2 ? "h2" : "h3";
  return (
    <div className={styles.root}>
      <div className={styles.icon} aria-hidden="true">{icon}</div>
      <Heading>{title}</Heading>
      <p>{description}</p>
      {action && <div className={styles.action}>{action}</div>}
    </div>
  );
}
