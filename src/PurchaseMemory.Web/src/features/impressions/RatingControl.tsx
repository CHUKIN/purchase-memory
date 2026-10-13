import { IonLabel, IonSegment, IonSegmentButton } from "@ionic/react";
import { Meh, ThumbsDown, ThumbsUp } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { ImpressionRating } from "../../api/types";
import { ratingLabel, ratings } from "./impressionText";

const icons = { dislike: ThumbsDown, neutral: Meh, like: ThumbsUp } as const;

export function RatingIcon({ rating, size = 16 }: { rating: ImpressionRating; size?: number }) {
  const Icon = icons[rating];
  return <Icon aria-hidden="true" size={size} />;
}

export interface RatingControlProps {
  /** Names the control for screen readers, and is shown above it unless `hideLegend`. */
  legend: string;
  value: ImpressionRating | null;
  onChange: (rating: ImpressionRating) => void;
  disabled?: boolean;
  hideLegend?: boolean;
}

/** The three-level scale as an iOS segmented control: «Не нравится · Средне · Нравится». */
export function RatingControl({ legend, value, onChange, disabled = false, hideLegend = false }: RatingControlProps) {
  const { t } = useTranslation();
  return (
    <div className="pm-rating">
      {!hideLegend && <span className="pm-field-label" aria-hidden="true">{legend}</span>}
      <IonSegment
        aria-label={legend}
        value={value ?? undefined}
        disabled={disabled}
        onIonChange={(event) => {
          const next = event.detail.value;
          if (next === "dislike" || next === "neutral" || next === "like") onChange(next);
        }}
      >
        {ratings.map((rating) => (
          <IonSegmentButton key={rating} value={rating} layout="icon-top">
            <RatingIcon rating={rating} />
            <IonLabel>{ratingLabel(t, rating)}</IonLabel>
          </IonSegmentButton>
        ))}
      </IonSegment>
    </div>
  );
}
