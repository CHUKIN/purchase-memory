import styles from "./SkeletonCard.module.css";

/**
 * A shimmering placeholder shaped like a `PurchaseFeedCard`, shown while its
 * real content is still loading. Purely decorative: the loading state itself
 * is announced separately, so this renders with `aria-hidden`.
 */
export function SkeletonCard() {
  return (
    <div className={styles.card} aria-hidden="true">
      <div className={styles.main}>
        <span className={`${styles.bar} ${styles.title}`} />
        <span className={`${styles.bar} ${styles.meta}`} />
      </div>
      <div className={styles.side}>
        <span className={`${styles.bar} ${styles.total}`} />
        <span className={`${styles.bar} ${styles.badge}`} />
      </div>
    </div>
  );
}
