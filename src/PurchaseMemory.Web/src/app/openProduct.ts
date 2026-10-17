import { useNavigate } from "react-router";
import { useReturnState } from "./backNavigation";
import { useRememberLine } from "./place";

/**
 * Opens a product's card from a purchase line, so «Назад» returns to this
 * screen: the open line is remembered (`lineIndex`, when a review card or a
 * line sheet is showing) and the card gets the return state.
 */
export function useOpenProduct(): (productId: string, lineIndex?: number) => void {
  const navigate = useNavigate();
  const returnState = useReturnState();
  const rememberLine = useRememberLine();
  return (productId, lineIndex) => {
    if (lineIndex !== undefined) rememberLine(lineIndex);
    navigate(`/products/${encodeURIComponent(productId)}`, { state: returnState });
  };
}
