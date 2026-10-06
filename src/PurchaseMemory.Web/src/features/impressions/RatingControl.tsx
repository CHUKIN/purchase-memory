import { Meh, ThumbsDown, ThumbsUp } from "lucide-react";
import { useId } from "react";
import { useTranslation } from "react-i18next";
import type { ImpressionRating } from "../../api/types";
import { ratingLabel, ratings } from "./impressionText";
import styles from "./Impressions.module.css";

const icons = { dislike: ThumbsDown, neutral: Meh, like: ThumbsUp } as const;

export function RatingIcon({ rating }: { rating: ImpressionRating }) {
  const Icon = icons[rating];
  return <Icon aria-hidden="true" />;
}

export interface RatingControlProps {
  /** Visible group label; it also names the radio group for screen readers. */
  legend: string;
  value: ImpressionRating | null;
  onChange: (rating: ImpressionRating) => void;
  disabled?: boolean;
  /** Hides the legend visually (it stays available to assistive technology). */
  hideLegend?: boolean;
}

/**
 * The three-level scale as a native radio group: arrow keys move between
 * options, and the choice is announced as «нравится, 3 из 3».
 */
export function RatingControl({ legend, value, onChange, disabled = false, hideLegend = false }: RatingControlProps) {
  const { t } = useTranslation();
  const name = useId();
  return (
    <fieldset className={styles.ratingGroup} disabled={disabled}>
      <legend className={hideLegend ? "visually-hidden" : undefined}>{legend}</legend>
      <div className={styles.ratingOptions}>
        {ratings.map((rating) => (
          <label key={rating} className={styles.ratingOption}>
            <input
              type="radio"
              name={name}
              value={rating}
              checked={value === rating}
              onChange={() => onChange(rating)}
            />
            <RatingIcon rating={rating} />
            {ratingLabel(t, rating)}
          </label>
        ))}
      </div>
    </fieldset>
  );
}
